using BookProtoAPI.Controllers.TreeView.DTOs;
using BookProtoAPI.Controllers.TreeView.Helpers;
using BookProtoAPI.Controllers.TreeView.Models;
using BookProtoAPI.Controllers.TreeView.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data;

namespace BookProtoAPI.Controllers.TreeView
{
    [ApiController]
    [Route("api/[controller]")]
    public class TreeViewController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ReportService _treeViewService = new ReportService();

        public TreeViewController(IConfiguration configuration, IMemoryCache cache)
        {
            _configuration = configuration;
            _cache = cache;
        }

        // Add this helper method
        private async Task<SqlConnection> OpenDatabaseConnection()
        {
            var connString = _configuration.GetConnectionString("DefaultConnection");
            var conn = new SqlConnection(connString);
            await conn.OpenAsync();
            return conn;
        }

        // Helper to get cached values and handle missing state
        private (IActionResult? Result, List<TreeSegment>? Segments, int RowCount) GetCachedValues(string segmentsKey, string rowCountKey)
        {
            if (!_cache.TryGetValue(segmentsKey, out List<TreeSegment> segments))
                return (BadRequest("Tree segment state not found."), null, 0);

            if (!_cache.TryGetValue(rowCountKey, out int rowCount))
                return (BadRequest("Tree row count state not found."), null, 0);

            return (null, segments, rowCount);
        }

        // Helper to construct cache keys
        private (string SegmentsKey, string RowCountKey) ConstructCacheKeys(string sessionKey)
        {
            return ($"TreeSegment_{sessionKey}", $"TreeRowCount_{sessionKey}");
        }

        [HttpPost("load")]
        public async Task<IActionResult> LoadTreeView([FromBody] TreeViewRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            var (segmentsKey, rowCountKey) = ConstructCacheKeys(request.SessionKey);

            // Open Database Connection
            using var conn = await OpenDatabaseConnection();

            // Add Root Segments
            (List<TreeSegment> segments, int countNodesInserted) = await AddRootSegmentsHelper.AddRootSegments(conn, request);

            // Check if segments are empty
            _cache.Set(segmentsKey, segments, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(20) });
            _cache.Set(rowCountKey, countNodesInserted, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(20) });

            // Get Tree View Results
            var results = await _treeViewService.GetTreeViewResults(conn, segments, request);

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = countNodesInserted,
                Results = results
            });
        }

        [HttpPost("expand")]
        public async Task<IActionResult> ExpandNode([FromBody] TreeNodeRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            var (segmentsKey, rowCountKey) = ConstructCacheKeys(request.SessionKey);

            // Get Cached Variables
            var (result, segments, rowCount) = GetCachedValues(segmentsKey, rowCountKey);
            if (result != null)
                return result;

            // Open Database Connection
            using var conn = await OpenDatabaseConnection();

            // Add Segments
            var addChildResult = await AddSegmentsHelper.AddSegments(request, segments, rowCount, conn);
            if (addChildResult.Result != null)
                return addChildResult.Result;

            rowCount = addChildResult.RowCount;

            // Save Cached Variables
            _cache.Set(segmentsKey, segments);
            _cache.Set(rowCountKey, rowCount);

            var treeViewRequest = new TreeViewRequest
            {
                RowsPerViewport = request.RowsPerViewport,
                ColumnsPerViewport = request.ColumnsPerViewport,
                FirstVisibleRow = request.FirstVisibleRow,
                FirstVisibleColumn = request.FirstVisibleColumn,
                GlobalSearchJobId = request.GlobalSearchJobId,
                RootID = 0,
                SessionKey = request.SessionKey
            };

            // Get Tree View Results
            var results = await _treeViewService.GetTreeViewResults(conn, segments, treeViewRequest);

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("collapse")]
        public async Task<IActionResult> CollapseNode([FromBody] TreeNodeRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            var (segmentsKey, rowCountKey) = ConstructCacheKeys(request.SessionKey);

            // Get Cached Values
            var (result, segments, rowCount) = GetCachedValues(segmentsKey, rowCountKey);
            if (result != null)
                return result;

            // Open Database Connection
            using var conn = await OpenDatabaseConnection();

            int deletedRows;
            // Remove Segments
            var removeResult = RemoveSegmentsHelper.RemoveSegments(request, segments, out deletedRows);
            if (removeResult != null)
                return removeResult;

            // Save Cached Values
            rowCount -= deletedRows;
            _cache.Set(segmentsKey, segments);
            _cache.Set(rowCountKey, rowCount);

            var treeViewRequest = new TreeViewRequest
            {
                RowsPerViewport = request.RowsPerViewport,
                ColumnsPerViewport = request.ColumnsPerViewport,
                FirstVisibleRow = request.FirstVisibleRow,
                FirstVisibleColumn = request.FirstVisibleColumn,
                GlobalSearchJobId = request.GlobalSearchJobId,
                RootID = 0,
                SessionKey = request.SessionKey
            };

            // Get Tree View Results
            var results = await _treeViewService.GetTreeViewResults(conn, segments, treeViewRequest);

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshNode([FromBody] TreeNodeRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            var (segmentsKey, rowCountKey) = ConstructCacheKeys(request.SessionKey);

            // Get Cached Variables
            var (result, segments, rowCount) = GetCachedValues(segmentsKey, rowCountKey);
            if (result != null)
                return result;

            // Open Database Connection
            using var conn = await OpenDatabaseConnection();

            int deletedRows;
            // Remove Segments
            var removeResult = RemoveSegmentsHelper.RemoveSegments(request, segments, out deletedRows);
            if (removeResult != null)
                return removeResult;

            // Save Cached Values
            rowCount -= deletedRows;

            // Add Segments
            var addChildResult = await AddSegmentsHelper.AddSegments(request, segments, rowCount, conn);
            if (addChildResult.Result != null)
                return addChildResult.Result;

            rowCount = addChildResult.RowCount;

            // Save Cached Variables
            _cache.Set(segmentsKey, segments);
            _cache.Set(rowCountKey, rowCount);

            var treeViewRequest = new TreeViewRequest
            {
                RowsPerViewport = request.RowsPerViewport,
                ColumnsPerViewport = request.ColumnsPerViewport,
                FirstVisibleRow = request.FirstVisibleRow,
                FirstVisibleColumn = request.FirstVisibleColumn,
                GlobalSearchJobId = request.GlobalSearchJobId,
                RootID = 0,
                SessionKey = request.SessionKey
            };

            // Get Tree View Results
            var results = await _treeViewService.GetTreeViewResults(conn, segments, treeViewRequest);

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("scroll")]
        public async Task<IActionResult> ScrollViewport([FromBody] TreeViewRequest request)
        {
            // Validate Request
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            var (segmentsKey, rowCountKey) = ConstructCacheKeys(request.SessionKey);

            // Get Cached Values
            var (result, segments, rowCount) = GetCachedValues(segmentsKey, rowCountKey);
            if (result != null)
                return result;

            // Open Database Connection
            using var conn = await OpenDatabaseConnection();

            // Get Tree View Results
            var results = await _treeViewService.GetTreeViewResults(conn, segments, request);

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("insert")]
        public async Task<IActionResult> InsertNode([FromBody] NodeRecord record)
        {
            // Validate the incoming record
            if (record == null)
                return BadRequest("Inserted record is required.");

            // Retrieve SessionKey from header
            if (!Request.Headers.TryGetValue("SessionKey", out var sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
                return BadRequest("SessionKey header is required.");

            // Optional: validate sessionKey here, e.g., check database or cache
            // if (!SessionValidator.IsValid(sessionKey)) return Unauthorized("Invalid session key.");

            try
            {
                // Open database connection
                using var conn = await OpenDatabaseConnection();

                // Perform the insert
                var crudService = new CrudService();
                var insertedNode = await crudService.Insert(conn, record);

                // Return the full inserted NodeRecord
                return Ok(insertedNode);
            }
            catch (SqlException sqlEx)
            {
                return Problem(
                    title: "Database error",
                    detail: sqlEx.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Unexpected error",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

        [HttpPost("update")]
        public async Task<IActionResult> UpdateNode([FromBody] NodeRecord record)
        {
            // Validate request body
            if (record == null)
                return BadRequest("Updated record is required.");

            // Validate session key from header
            if (!Request.Headers.TryGetValue("SessionKey", out var sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
                return BadRequest("SessionKey header is required.");

            try
            {
                // Open database connection
                using var conn = await OpenDatabaseConnection();

                // Perform the update and retrieve the upserted record
                var crudService = new CrudService();
                var updatedNode = await crudService.Update(conn, record);

                // Return the updated NodeRecord directly
                return Ok(updatedNode);
            }
            catch (SqlException sqlEx)
            {
                return Problem(
                    title: "Database error",
                    detail: sqlEx.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Unexpected error",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }


        [HttpPost("delete")]
        public async Task<IActionResult> DeleteNode([FromBody] NodeRecord record)
        {
            // Validate request
            if (record == null)
                return BadRequest("Deleted record is required.");

            // Validate session key
            if (!Request.Headers.TryGetValue("SessionKey", out var sessionKey) || string.IsNullOrWhiteSpace(sessionKey))
                return BadRequest("SessionKey header is required.");

            try
            {
                // Open DB connection
                using var conn = await OpenDatabaseConnection();

                // Perform deletion
                var crudService = new CrudService();
                var deletedRecord = await crudService.Delete(conn, record);

                // Return deleted record
                return Ok(deletedRecord);
            }
            catch (SqlException sqlEx)
            {
                return Problem(
                    title: "Database error during deletion",
                    detail: sqlEx.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
            catch (Exception ex)
            {
                return Problem(
                    title: "Unexpected error during deletion",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }
        }

    }
}
