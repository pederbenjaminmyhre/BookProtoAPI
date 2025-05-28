using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Xml.Linq;
using BookProtoAPI.Controllers.DTOs;
using BookProtoAPI.Controllers.Models;

namespace BookProtAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TreeViewController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public TreeViewController(IConfiguration configuration, IMemoryCache cache)
        {
            _configuration = configuration;
            _cache = cache;
        }

        [HttpPost("load")]
        public async Task<IActionResult> LoadTreeView([FromBody] TreeViewRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            string segmentsKey = $"TreeSegment_{request.SessionKey}";
            string rowCountKey = $"TreeRowCount_{request.SessionKey}";

            // Declarations
            var segments = new List<TreeSegment>();
            int countNodesInserted = 0;
            int segmentId = 1;
            int segmentPosition = 1;
            int firstTreeRow = 1;

            // Open Database Connection
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Add Any Staged Root Segments
            using (var cmd = new SqlCommand("SELECT ParentID, StageDate, CurrentSequenceNumber FROM PerParentSequence WHERE ParentID = @ParentID ORDER BY StageDate", conn))
            {
                cmd.Parameters.AddWithValue("@ParentID", request.RootID);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var stagedCount = reader.GetInt32(2); // Number of rows in staged segment
                    segments.Add(new TreeSegment
                    {
                        SegmentID = segmentId++, // This assigns the value before incrementation
                        ParentSegmentID = 0, // Root segments don't have a ParentSegment
                        SegmentPosition = segmentPosition++, // This assigns the value before incrementation
                        ParentID = request.RootID,
                        TreeLevel = 1,
                        StageDate = reader.GetDateTime(1), // Column 1 is the stage date
                        RecordCount = stagedCount,
                        FirstTreeRow = firstTreeRow,
                        LastTreeRow = firstTreeRow + stagedCount - 1,
                        FirstSortID = 1,
                        LastSortID = stagedCount
                    });
                    firstTreeRow += stagedCount;
                    countNodesInserted += stagedCount;
                }
            }

            // Add the Processed Segment
            using (var cmd = new SqlCommand("SELECT ISNULL(MAX(SortID), 0) FROM Reporting WHERE ParentID = @ParentID", conn))
            {
                cmd.Parameters.AddWithValue("@ParentID", request.RootID);
                var processedCount = (int)await cmd.ExecuteScalarAsync();

                if (processedCount > 0)
                {
                    segments.Add(new TreeSegment
                    {
                        SegmentID = segmentId++,
                        ParentSegmentID = 0,
                        SegmentPosition = segmentPosition++,
                        ParentID = request.RootID,
                        TreeLevel = 1,
                        StageDate = new DateTime(1900, 1, 1),
                        RecordCount = processedCount,
                        FirstTreeRow = firstTreeRow,
                        LastTreeRow = firstTreeRow + processedCount - 1,
                        FirstSortID = 1,
                        LastSortID = processedCount
                    });
                    firstTreeRow += processedCount;
                    countNodesInserted += processedCount;
                }
            }

            _cache.Set(segmentsKey, segments, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(20) });
            _cache.Set(rowCountKey, countNodesInserted, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(20) });

            int lastVisibleRow = request.FirstVisibleRow + request.RowsPerViewport - 1;

            // Get Intersecting Segments
            var intersecting = segments
                .Where(s => request.FirstVisibleRow <= s.LastTreeRow && lastVisibleRow >= s.FirstTreeRow)
                .Select(s => new TreeSegment
                {
                    SegmentID = s.SegmentID,
                    ParentSegmentID = s.ParentSegmentID,
                    SegmentPosition = s.SegmentPosition,
                    ParentID = s.ParentID,
                    TreeLevel = s.TreeLevel,
                    StageDate = s.StageDate,
                    RecordCount = s.RecordCount,
                    FirstTreeRow = s.FirstTreeRow,
                    LastTreeRow = s.LastTreeRow,
                    FirstSortID = s.FirstSortID,
                    LastSortID = s.LastSortID
                })
                .ToList();

            // Apply Offsets
            if (intersecting.Count > 0)
            {
                var first = intersecting[0];
                int offsetFirst = first.FirstTreeRow - first.FirstSortID;
                
                var last = intersecting[^1];
                int offsetLast = last.FirstTreeRow - last.FirstSortID;

                first.FirstSortID = request.FirstVisibleRow - offsetFirst;
                last.LastSortID = lastVisibleRow - offsetLast;
            }

            // Prepare Database Table Parameter
            var queryList = new DataTable();
            queryList.Columns.Add("RowNumber", typeof(int));
            queryList.Columns.Add("StageDate", typeof(DateTime));
            queryList.Columns.Add("ParentID", typeof(int));
            queryList.Columns.Add("FirstSortID", typeof(int));
            queryList.Columns.Add("LastSortID", typeof(int));
            queryList.Columns.Add("TreeLevel", typeof(int));

            // Populate Database Table Parameter
            int rowNum = 1;
            foreach (var seg in intersecting)
            {
                queryList.Rows.Add(rowNum++, seg.StageDate, seg.ParentID, seg.FirstSortID, seg.LastSortID, seg.TreeLevel);
            }

            // Query the Database
            var results = new List<TreeNodeResult>();
            using (var cmd = new SqlCommand("dbo.GetTreeViewResults", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@QueryList", queryList);
                cmd.Parameters.AddWithValue("@FirstVisibleColumn", request.FirstVisibleColumn);
                cmd.Parameters.AddWithValue("@ColumnsPerViewport", request.ColumnsPerViewport);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new TreeNodeResult
                    {
                        ID = reader.GetInt32(1),
                        ParentID = reader.GetInt32(0),
                        SortID = reader.GetInt32(3),
                        TreeLevel = reader.GetInt32(2),
                        HasChildren = reader.GetBoolean(4),
                        ChildCount = reader.GetInt32(5),
                        StageDate = reader.GetDateTime(6),
                        IsExpanded = reader.GetBoolean(7),
                        Name = reader.GetString(8)
                    });
                }
            }

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = countNodesInserted,
                Results = results
            });
        }

        [HttpPost("expand")]
        public async Task<IActionResult> ExpandNode([FromBody] TreeNodeExpandRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            string segmentsKey = $"TreeSegment_{request.SessionKey}";
            string rowCountKey = $"TreeRowCount_{request.SessionKey}";

            // Get Cached Variables
            if (!_cache.TryGetValue(segmentsKey, out List<TreeSegment> segments))
                return BadRequest("Tree segment state not found.");

            if (!_cache.TryGetValue(rowCountKey, out int rowCount))
                return BadRequest("Tree row count state not found.");

            // Open Database Connection
            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            // Get expandedNodeSegment
            var expandedNodeSegment = segments.Find(s =>
                s.ParentID == request.ExpandedNode.ParentID &&
                s.StageDate == request.ExpandedNode.StageDate &&
                request.ExpandedNode.SortID >= s.FirstSortID &&
                request.ExpandedNode.SortID <= s.LastSortID);

            if (expandedNodeSegment == null)
                return BadRequest("Expanded node does not fall within any known segment.");

            // Find out if Split Is Required
            var siblings = segments.FindAll(s => s.ParentID == expandedNodeSegment.ParentID);
            var lastSibling = siblings[^1];
            bool isLastNode = (lastSibling.SegmentID == expandedNodeSegment.SegmentID) &&
                              (request.ExpandedNode.SortID == expandedNodeSegment.LastSortID);
            bool splitIsRequired = !isLastNode;

            // If splitIsRequired, modify the expandedNodeSegment
            int nodesBeforeInsert = 0;
            int nodesAfterInsert = 0;
            if (splitIsRequired)
            {
                nodesBeforeInsert = request.ExpandedNode.SortID - expandedNodeSegment.FirstSortID + 1;
                nodesAfterInsert = expandedNodeSegment.LastSortID - request.ExpandedNode.SortID;

                expandedNodeSegment.RecordCount = nodesBeforeInsert;
                expandedNodeSegment.LastTreeRow = expandedNodeSegment.FirstTreeRow + nodesBeforeInsert - 1;
                expandedNodeSegment.LastSortID = expandedNodeSegment.FirstSortID + nodesBeforeInsert - 1;
            }

            int insertedRows = request.ExpandedNode.ChildCount;
            int insertIndex = segments.IndexOf(expandedNodeSegment) + 1;
            int nextSegmentId = segments.Max(s => s.SegmentID) + 1;
            int nextPosition = expandedNodeSegment.SegmentPosition + 1;
            int nextTreeRow = expandedNodeSegment.LastTreeRow + 1;

            // Add Staged Child Segments
            var stagedChildren = new List<TreeSegment>();
            using (var cmd = new SqlCommand("SELECT ParentID, StageDate, CurrentSequenceNumber FROM PerParentSequence WHERE ParentID = @ParentID ORDER BY StageDate", conn))
            {
                cmd.Parameters.AddWithValue("@ParentID", request.ExpandedNode.ID);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int count = reader.GetInt32(2);
                    stagedChildren.Add(new TreeSegment
                    {
                        SegmentID = nextSegmentId++,
                        ParentSegmentID = expandedNodeSegment.SegmentID,
                        SegmentPosition = nextPosition++,
                        ParentID = request.ExpandedNode.ID,
                        TreeLevel = request.ExpandedNode.TreeLevel + 1,
                        StageDate = reader.GetDateTime(1),
                        RecordCount = count,
                        FirstTreeRow = nextTreeRow,
                        LastTreeRow = nextTreeRow + count - 1,
                        FirstSortID = 1,
                        LastSortID = count
                    });
                    nextTreeRow += count;
                    insertedRows += count;
                }
            }

            // Add processed child segment
            stagedChildren.Add(new TreeSegment
            {
                SegmentID = nextSegmentId++,
                ParentSegmentID = expandedNodeSegment.SegmentID,
                SegmentPosition = nextPosition++,
                ParentID = request.ExpandedNode.ID,
                TreeLevel = request.ExpandedNode.TreeLevel + 1,
                StageDate = new DateTime(1900, 1, 1),
                RecordCount = request.ExpandedNode.ChildCount,
                FirstTreeRow = nextTreeRow,
                LastTreeRow = nextTreeRow + request.ExpandedNode.ChildCount - 1,
                FirstSortID = 1,
                LastSortID = request.ExpandedNode.ChildCount
            });

            nextTreeRow += request.ExpandedNode.ChildCount;

            // Add Split Segment
            if (splitIsRequired)
            {
                stagedChildren.Add(new TreeSegment
                {
                    SegmentID = nextSegmentId++,
                    ParentSegmentID = expandedNodeSegment.ParentSegmentID,
                    SegmentPosition = nextPosition++,
                    ParentID = expandedNodeSegment.ParentID,
                    TreeLevel = expandedNodeSegment.TreeLevel,
                    StageDate = expandedNodeSegment.StageDate,
                    RecordCount = nodesAfterInsert,
                    FirstTreeRow = nextTreeRow,
                    LastTreeRow = nextTreeRow + nodesAfterInsert - 1,
                    FirstSortID = expandedNodeSegment.FirstSortID + nodesBeforeInsert,
                    LastSortID = nodesBeforeInsert + nodesAfterInsert
                });

                nextTreeRow += nodesAfterInsert;
            }

            // Shift subsequent segments
            for (int i = insertIndex; i < segments.Count; i++)
            {
                segments[i].SegmentPosition += stagedChildren.Count + 1;
                segments[i].FirstTreeRow += insertedRows;
                segments[i].LastTreeRow += insertedRows;
            }

            // Insert new segments in-place
            segments.InsertRange(insertIndex, stagedChildren);
            rowCount += insertedRows;

            // Save Cached Variables
            _cache.Set(segmentsKey, segments);
            _cache.Set(rowCountKey, rowCount);

            // Copy Intersecting Segments
            int lastVisibleRow = request.FirstVisibleRow + request.RowsPerViewport - 1;
            var intersecting = segments
                .Where(s => request.FirstVisibleRow <= s.LastTreeRow && lastVisibleRow >= s.FirstTreeRow)
                .Select(s => new TreeSegment
                {
                    SegmentID = s.SegmentID,
                    ParentSegmentID = s.ParentSegmentID,
                    SegmentPosition = s.SegmentPosition,
                    ParentID = s.ParentID,
                    TreeLevel = s.TreeLevel,
                    StageDate = s.StageDate,
                    RecordCount = s.RecordCount,
                    FirstTreeRow = s.FirstTreeRow,
                    LastTreeRow = s.LastTreeRow,
                    FirstSortID = s.FirstSortID,
                    LastSortID = s.LastSortID
                })
                .ToList();

            // Apply Offsets
            if (intersecting.Count > 0)
            {
                var first = intersecting[0];
                int offsetFirst = first.FirstTreeRow - first.FirstSortID;

                var last = intersecting[^1];
                int offsetLast = last.FirstTreeRow - last.FirstSortID;

                first.FirstSortID = request.FirstVisibleRow - offsetFirst;
                last.LastSortID = lastVisibleRow - offsetLast;
            }

            // Declare Database Table Parameter
            var queryList = new DataTable();
            queryList.Columns.Add("RowNumber", typeof(int));
            queryList.Columns.Add("StageDate", typeof(DateTime));
            queryList.Columns.Add("ParentID", typeof(int));
            queryList.Columns.Add("FirstSortID", typeof(int));
            queryList.Columns.Add("LastSortID", typeof(int));
            queryList.Columns.Add("TreeLevel", typeof(int));

            // Populate Database Table Parameter
            int rowNum = 1;
            foreach (var seg in intersecting)
            {
                queryList.Rows.Add(rowNum++, seg.StageDate, seg.ParentID, seg.FirstSortID, seg.LastSortID, seg.TreeLevel);
            }

            // Query the Database
            var results = new List<TreeNodeResult>();
            using (var cmd = new SqlCommand("dbo.GetTreeViewResults", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@QueryList", queryList);
                cmd.Parameters.AddWithValue("@FirstVisibleColumn", request.FirstVisibleColumn);
                cmd.Parameters.AddWithValue("@ColumnsPerViewport", request.ColumnsPerViewport);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new TreeNodeResult
                    {
                        ID = reader.GetInt32(1),
                        ParentID = reader.GetInt32(0),
                        SortID = reader.GetInt32(3),
                        TreeLevel = reader.GetInt32(2),
                        HasChildren = reader.GetBoolean(4),
                        ChildCount = reader.GetInt32(5),
                        StageDate = reader.GetDateTime(6),
                        IsExpanded = reader.GetBoolean(7),
                        Name = reader.GetString(8)
                    });
                }
            }

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("collapse")]
        public async Task<IActionResult> CollapseNode([FromBody] TreeNodeCollapseRequest request)
        {
            // Validate Session Key
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            // Construct Cache Keys
            string segmentsKey = $"TreeSegment_{request.SessionKey}";
            string rowCountKey = $"TreeRowCount_{request.SessionKey}";

            // Get Cached Variables
            if (!_cache.TryGetValue(segmentsKey, out List<TreeSegment> segments))
                return BadRequest("Tree segment state not found.");

            if (!_cache.TryGetValue(rowCountKey, out int rowCount))
                return BadRequest("Tree row count state not found.");

            // Get the Collapsed Segment
            var collapsedSegment = segments.Find(s =>
                s.ParentID == request.CollapsedNode.ParentID &&
                s.StageDate == request.CollapsedNode.StageDate &&
                request.CollapsedNode.SortID >= s.FirstSortID &&
                request.CollapsedNode.SortID <= s.LastSortID);
            if (collapsedSegment == null)
                return BadRequest("Collapsed node does not match any known segment.");

            // Decide if segment must be merged
            var siblings = segments.FindAll(s => s.ParentID == collapsedSegment.ParentID);
            var lastSibling = siblings[^1];
            bool isLastNode = lastSibling.SegmentID == collapsedSegment.SegmentID &&
                              request.CollapsedNode.SortID == collapsedSegment.LastSortID;
            bool mergeIsRequired = !isLastNode;

            // Declarations
            bool hadSplitSegment = false;
            int deletedRows = 0;
            int deletedSegments = 0;

            // Collapse location
            int collapsedIndex = segments.IndexOf(collapsedSegment);
            TreeSegment? splitSegment = null;

            // Merge values from splitSegment to collapsedSegment
            if (mergeIsRequired)
            {
                var splitSegmentIndex = segments.FindIndex(s =>
                    s.SegmentPosition > collapsedSegment.SegmentPosition &&
                    s.ParentID == collapsedSegment.ParentID);

                if (splitSegmentIndex >= 0)
                {
                    splitSegment = segments[splitSegmentIndex];
                    collapsedSegment.RecordCount += splitSegment.RecordCount;
                    collapsedSegment.LastTreeRow = collapsedSegment.FirstTreeRow + collapsedSegment.RecordCount - 1;
                    collapsedSegment.LastSortID = collapsedSegment.FirstSortID + collapsedSegment.RecordCount - 1;

                    segments.RemoveAt(splitSegmentIndex);
                    hadSplitSegment = true;
                    deletedSegments++;
                }
            }

            // Collect segments to remove
            int baseIndex = segments.FindIndex(s => s.SegmentID == collapsedSegment.SegmentID);
            var segmentsToDelete = new List<TreeSegment>();

            for (int i = baseIndex + 1; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.TreeLevel > collapsedSegment.TreeLevel)
                {
                    segmentsToDelete.Add(seg);
                    deletedRows += seg.RecordCount;
                }
                else
                {
                    break;
                }
            }

            // Remove segments
            foreach (var seg in segmentsToDelete)
                segments.Remove(seg);

            // Count deleted segments
            deletedSegments += segmentsToDelete.Count;

            // Shift nodes up
            for (int i = collapsedIndex + 1; i < segments.Count; i++)
            {
                segments[i].SegmentPosition -= deletedSegments;
                segments[i].FirstTreeRow -= deletedRows;
                segments[i].LastTreeRow -= deletedRows;
            }

            // Save segment table and row count
            rowCount -= deletedRows;
            _cache.Set(segmentsKey, segments);
            _cache.Set(rowCountKey, rowCount);

            // Copy Intersecting Segments
            int lastVisibleRow = request.FirstVisibleRow + request.RowsPerViewport - 1;
            var intersecting = segments
                .Where(s => request.FirstVisibleRow <= s.LastTreeRow && lastVisibleRow >= s.FirstTreeRow)
                .Select(s => new TreeSegment
                {
                    SegmentID = s.SegmentID,
                    ParentSegmentID = s.ParentSegmentID,
                    SegmentPosition = s.SegmentPosition,
                    ParentID = s.ParentID,
                    TreeLevel = s.TreeLevel,
                    StageDate = s.StageDate,
                    RecordCount = s.RecordCount,
                    FirstTreeRow = s.FirstTreeRow,
                    LastTreeRow = s.LastTreeRow,
                    FirstSortID = s.FirstSortID,
                    LastSortID = s.LastSortID
                })
                .ToList();

            // Apply Offsets
            if (intersecting.Count > 0)
            {
                var first = intersecting[0];
                int offsetFirst = first.FirstTreeRow - first.FirstSortID;

                var last = intersecting[^1];
                int offsetLast = last.FirstTreeRow - last.FirstSortID;

                first.FirstSortID = request.FirstVisibleRow - offsetFirst;
                last.LastSortID = lastVisibleRow - offsetLast;
            }

            // Declare Database Table Parameter
            var queryList = new DataTable();
            queryList.Columns.Add("RowNumber", typeof(int));
            queryList.Columns.Add("StageDate", typeof(DateTime));
            queryList.Columns.Add("ParentID", typeof(int));
            queryList.Columns.Add("FirstSortID", typeof(int));
            queryList.Columns.Add("LastSortID", typeof(int));
            queryList.Columns.Add("TreeLevel", typeof(int));

            // Populate Database Table Parameter
            int rowNum = 1;
            foreach (var seg in intersecting)
            {
                queryList.Rows.Add(rowNum++, seg.StageDate, seg.ParentID, seg.FirstSortID, seg.LastSortID, seg.TreeLevel);
            }

            // Open Database Connection
            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            // Query the database
            var results = new List<TreeNodeResult>();
            using (var cmd = new SqlCommand("dbo.GetTreeViewResults", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@QueryList", queryList);
                cmd.Parameters.AddWithValue("@FirstVisibleColumn", request.FirstVisibleColumn);
                cmd.Parameters.AddWithValue("@ColumnsPerViewport", request.ColumnsPerViewport);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new TreeNodeResult
                    {
                        ID = reader.GetInt32(1),
                        ParentID = reader.GetInt32(0),
                        SortID = reader.GetInt32(3),
                        TreeLevel = reader.GetInt32(2),
                        HasChildren = reader.GetBoolean(4),
                        ChildCount = reader.GetInt32(5),
                        StageDate = reader.GetDateTime(6),
                        IsExpanded = reader.GetBoolean(7),
                        Name = reader.GetString(8)
                    });
                }
            }

            // Return Query Results
            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshNode([FromBody] TreeNodeRefreshRequest request)
        {
            var collapseRequest = new TreeNodeCollapseRequest
            {
                SessionKey = request.SessionKey,
                RowsPerViewport = request.RowsPerViewport,
                ColumnsPerViewport = request.ColumnsPerViewport,
                FirstVisibleRow = request.FirstVisibleRow,
                FirstVisibleColumn = request.FirstVisibleColumn,
                CollapsedNode = new CollapsedNode
                {
                    ParentID = request.RefreshedNode.ParentID,
                    ID = request.RefreshedNode.ID,
                    TreeLevel = request.RefreshedNode.TreeLevel,
                    ChildCount = request.RefreshedNode.ChildCount,
                    SortID = request.RefreshedNode.SortID,
                    StageDate = request.RefreshedNode.StageDate
                }
            };

            var collapseResult = await CollapseNode(collapseRequest);
            if (collapseResult is BadRequestObjectResult badCollapse)
                return badCollapse;

            var expandRequest = new TreeNodeExpandRequest
            {
                SessionKey = request.SessionKey,
                RowsPerViewport = request.RowsPerViewport,
                ColumnsPerViewport = request.ColumnsPerViewport,
                FirstVisibleRow = request.FirstVisibleRow,
                FirstVisibleColumn = request.FirstVisibleColumn,
                ExpandedNode = new ExpandedNode
                {
                    ParentID = request.RefreshedNode.ParentID,
                    ID = request.RefreshedNode.ID,
                    TreeLevel = request.RefreshedNode.TreeLevel,
                    ChildCount = request.RefreshedNode.ChildCount,
                    SortID = request.RefreshedNode.SortID,
                    StageDate = request.RefreshedNode.StageDate
                }
            };

            return await ExpandNode(expandRequest);
        }

        [HttpPost("scroll")]
        public async Task<IActionResult> ScrollViewport([FromBody] TreeViewRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SessionKey))
                return BadRequest("SessionKey is required");

            string segmentsKey = $"TreeSegment_{request.SessionKey}";
            string rowCountKey = $"TreeRowCount_{request.SessionKey}";

            if (!_cache.TryGetValue(segmentsKey, out List<TreeSegment> segments))
                return BadRequest("Tree segment state not found.");

            if (!_cache.TryGetValue(rowCountKey, out int rowCount))
                return BadRequest("Tree row count state not found.");

            int lastVisibleRow = request.FirstVisibleRow + request.RowsPerViewport - 1;

            var intersecting = segments
                .Where(s => request.FirstVisibleRow <= s.LastTreeRow && lastVisibleRow >= s.FirstTreeRow)
                .Select(s => new TreeSegment
                {
                    SegmentID = s.SegmentID,
                    ParentSegmentID = s.ParentSegmentID,
                    SegmentPosition = s.SegmentPosition,
                    ParentID = s.ParentID,
                    TreeLevel = s.TreeLevel,
                    StageDate = s.StageDate,
                    RecordCount = s.RecordCount,
                    FirstTreeRow = s.FirstTreeRow,
                    LastTreeRow = s.LastTreeRow,
                    FirstSortID = s.FirstSortID,
                    LastSortID = s.LastSortID
                })
                .ToList();

            if (intersecting.Count > 0)
            {
                var first = intersecting[0];
                int offsetFirst = first.FirstTreeRow - first.FirstSortID;

                var last = intersecting[^1];
                int offsetLast = last.FirstTreeRow - last.FirstSortID;

                first.FirstSortID = request.FirstVisibleRow - offsetFirst;
                last.LastSortID = lastVisibleRow - offsetLast;
            }

            var queryList = new DataTable();
            queryList.Columns.Add("RowNumber", typeof(int));
            queryList.Columns.Add("StageDate", typeof(DateTime));
            queryList.Columns.Add("ParentID", typeof(int));
            queryList.Columns.Add("FirstSortID", typeof(int));
            queryList.Columns.Add("LastSortID", typeof(int));
            queryList.Columns.Add("TreeLevel", typeof(int));

            int rowNum = 1;
            foreach (var seg in intersecting)
            {
                queryList.Rows.Add(rowNum++, seg.StageDate, seg.ParentID, seg.FirstSortID, seg.LastSortID, seg.TreeLevel);
            }

            var results = new List<TreeNodeResult>();
            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            using (var cmd = new SqlCommand("dbo.GetTreeViewResults", conn))
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@QueryList", queryList);
                cmd.Parameters.AddWithValue("@FirstVisibleColumn", request.FirstVisibleColumn);
                cmd.Parameters.AddWithValue("@ColumnsPerViewport", request.ColumnsPerViewport);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new TreeNodeResult
                    {
                        ID = reader.GetInt32(1),
                        ParentID = reader.GetInt32(0),
                        SortID = reader.GetInt32(3),
                        TreeLevel = reader.GetInt32(2),
                        HasChildren = reader.GetBoolean(4),
                        ChildCount = reader.GetInt32(5),
                        StageDate = reader.GetDateTime(6),
                        IsExpanded = reader.GetBoolean(7),
                        Name = reader.GetString(8)
                    });
                }
            }

            return Ok(new
            {
                TreeRowCount = rowCount,
                Results = results
            });
        }

        [HttpPost("insert")]
        public async Task<IActionResult> InsertNode([FromBody] InsertedRecord record)
        {
            if (record == null)
                return BadRequest("Inserted record is required.");

            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.InsertTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ParentID", record.ParentID);
            cmd.Parameters.AddWithValue("@HasChildren", record.HasChildren);
            cmd.Parameters.AddWithValue("@ChildCount", record.ChildCount);
            cmd.Parameters.AddWithValue("@Name", record.Name);
            cmd.Parameters.AddWithValue("@StageDate", record.StageDate);

            var idParam = new SqlParameter("@GeneratedID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            var sortIdParam = new SqlParameter("@GeneratedSortID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            cmd.Parameters.Add(idParam);
            cmd.Parameters.Add(sortIdParam);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new
            {
                Success = true,
                ID = (int)idParam.Value,
                SortID = (int)sortIdParam.Value
            });
        }

        [HttpPost("update")]
        public async Task<IActionResult> UpdateNode([FromBody] UpdatedRecord record)
        {
            if (record == null)
                return BadRequest("Updated record is required.");

            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.UpdateTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ID", record.ID);
            cmd.Parameters.AddWithValue("@NewParentID", record.ParentID);
            cmd.Parameters.AddWithValue("@HasChildren", record.HasChildren);
            cmd.Parameters.AddWithValue("@ChildCount", record.ChildCount);
            cmd.Parameters.AddWithValue("@Name", record.Name);
            cmd.Parameters.AddWithValue("@StageDate", record.StageDate);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new
            {
                Success = true
            });
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteNode([FromBody] DeletedRecord record)
        {
            if (record == null)
                return BadRequest("Deleted record is required.");

            var connString = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.DeleteTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ID", record.ID);
            cmd.Parameters.AddWithValue("@StageDate", record.StageDate);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new { Success = true });
        }
    }
}
