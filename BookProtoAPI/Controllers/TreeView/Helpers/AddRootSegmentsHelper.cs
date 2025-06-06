using System;
using System.Collections.Generic;
// using System.Data.SqlClient; // Remove this
using Microsoft.Data.SqlClient; // Use Microsoft.Data.SqlClient instead
using System.Threading.Tasks;
using BookProtoAPI.Controllers.TreeView.DTOs;
using BookProtoAPI.Controllers.TreeView.Models;

namespace BookProtoAPI.Controllers.TreeView.Helpers
{
    public static class AddRootSegmentsHelper
    {
        public static async Task<(List<TreeSegment> segments, int countNodesInserted)> AddRootSegments(SqlConnection conn, TreeViewRequest request)
        {
            var segments = new List<TreeSegment>();
            int countNodesInserted = 0;
            int segmentId = 1;
            int segmentPosition = 1;
            int firstTreeRow = 1;

            // Add Staged Root Segments
            (countNodesInserted, segmentId, segmentPosition, firstTreeRow) = await AddStagedRootSegments(conn, request, segments, countNodesInserted, segmentId, segmentPosition, firstTreeRow);

            // Add Processed Root Segment
            (countNodesInserted, segmentId, segmentPosition, firstTreeRow) = await AddProcessedRootSegment(conn, request, segments, countNodesInserted, segmentId, segmentPosition, firstTreeRow);

            return (segments, countNodesInserted);
        }
        private static async Task<(int countNodesInserted, int segmentId, int segmentPosition, int firstTreeRow)> AddStagedRootSegments(SqlConnection conn, TreeViewRequest request, List<TreeSegment> segments, int countNodesInserted, int segmentId, int segmentPosition, int firstTreeRow)
        {
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
                        TreeDepth = 1,
                        StageDate = DateOnly.FromDateTime(reader.GetDateTime(1)), // Convert DateTime to DateOnly
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

            return (countNodesInserted, segmentId, segmentPosition, firstTreeRow);
        }
        private static async Task<(int countNodesInserted, int segmentId, int segmentPosition, int firstTreeRow)> AddProcessedRootSegment(SqlConnection conn, TreeViewRequest request, List<TreeSegment> segments, int countNodesInserted, int segmentId, int segmentPosition, int firstTreeRow)
        {
            using (var cmd = new SqlCommand("SELECT ISNULL(MAX(SortID), 0) FROM Reporting WHERE ParentID = @ParentID", conn))
            {
                cmd.Parameters.AddWithValue("@ParentID", request.RootID);
                var processedCountObj = await cmd.ExecuteScalarAsync();
                int processedCount = 0;
                if (processedCountObj != null && processedCountObj != DBNull.Value)
                {
                    processedCount = Convert.ToInt32(processedCountObj);
                }

                if (processedCount > 0)
                {
                    segments.Add(new TreeSegment
                    {
                        SegmentID = segmentId++,
                        ParentSegmentID = 0,
                        SegmentPosition = segmentPosition++,
                        ParentID = request.RootID,
                        TreeDepth = 1,
                        StageDate = new DateOnly(1900, 1, 1),
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

            return (countNodesInserted, segmentId, segmentPosition, firstTreeRow);
        }


    }
}
