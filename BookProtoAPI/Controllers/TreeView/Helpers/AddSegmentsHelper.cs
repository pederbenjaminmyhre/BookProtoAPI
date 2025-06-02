using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using BookProtoAPI.Controllers.TreeView.DTOs;
using BookProtoAPI.Controllers.TreeView.Models;

namespace BookProtoAPI.Controllers.TreeView.Helpers
{
    public static class AddSegmentsHelper
    {
        public record AddChildNodesResult(IActionResult? Result, int RowCount);

        public static async Task<AddChildNodesResult> AddSegments(
            TreeNodeRequest request,
            List<TreeSegment> segments,
            int rowCount,
            SqlConnection conn)
        {
            // Get expandedNodeSegment
            TreeSegment? expandedNodeSegment = GetExpandedNodeSegment(request, segments);

            // Validate
            if (expandedNodeSegment == null)
                return new AddChildNodesResult(new BadRequestObjectResult("Expanded node does not fall within any known segment."), rowCount);

            // Is Split Required?
            bool splitIsRequired = IsSplitRequired(request, segments, expandedNodeSegment);

            // If splitIsRequired
            int nodesBeforeInsert = 0;
            int nodesAfterInsert = 0;
            if (splitIsRequired)
            {
                // Split Record Count
                SplitRecordCount(request, expandedNodeSegment, out nodesBeforeInsert, out nodesAfterInsert);

                // Modify expandedNodeSegment
                ModifyExpandedNodeSegment(expandedNodeSegment, nodesBeforeInsert);
            }

            // Declarations
            int insertedRows = request.ChildCount;
            int insertIndex = segments.IndexOf(expandedNodeSegment) + 1;
            int nextSegmentId = segments.Max(s => s.SegmentID) + 1;
            int nextPosition = expandedNodeSegment.SegmentPosition + 1;
            int nextTreeRow = expandedNodeSegment.LastTreeRow + 1;

            // Add Staged Segments
            var stagedChildren = new List<TreeSegment>();
            (insertedRows, nextSegmentId, nextPosition, nextTreeRow) = await AddStagedSegments(request, conn, expandedNodeSegment, insertedRows, nextSegmentId, nextPosition, nextTreeRow, stagedChildren);

            // Add Processed Segment
            AddProcessedSegment(request, expandedNodeSegment, ref nextSegmentId, ref nextPosition, nextTreeRow, stagedChildren);

            // Add ChildCount
            nextTreeRow += request.ChildCount;

            // Add Split Segment
            AddSplitSegment(expandedNodeSegment, splitIsRequired, nodesBeforeInsert, nodesAfterInsert, ref nextSegmentId, ref nextPosition, ref nextTreeRow, stagedChildren);

            // Shift Subsequent Segments Down
            ShiftSubsequentSegmentsDown(segments, insertedRows, insertIndex, stagedChildren);

            // Insert New Segments
            segments.InsertRange(insertIndex, stagedChildren);
            rowCount += insertedRows;

            // All code paths now return a value
            return new AddChildNodesResult(null, rowCount);
        }
        private static TreeSegment? GetExpandedNodeSegment(TreeNodeRequest request, List<TreeSegment> segments)
        {
            // Get expandedNodeSegment
            return segments.Find((Predicate<TreeSegment>)(s =>
                s.ParentID == request.ParentID &&
                s.StageDate == request.StageDate &&
                request.SortID >= s.FirstSortID &&
                request.SortID <= s.LastSortID));
        }
        private static bool IsSplitRequired(TreeNodeRequest request, List<TreeSegment> segments, TreeSegment expandedNodeSegment)
        {
            var siblings = segments.FindAll(s => s.ParentID == expandedNodeSegment.ParentID);
            var lastSibling = siblings[^1];
            bool isLastNode = lastSibling.SegmentID == expandedNodeSegment.SegmentID &&
                              request.SortID == expandedNodeSegment.LastSortID;
            bool splitIsRequired = !isLastNode;
            return splitIsRequired;
        }
        private static void SplitRecordCount(TreeNodeRequest request, TreeSegment expandedNodeSegment, out int nodesBeforeInsert, out int nodesAfterInsert)
        {
            nodesBeforeInsert = request.SortID - expandedNodeSegment.FirstSortID + 1;
            nodesAfterInsert = expandedNodeSegment.LastSortID - request.SortID;
        }
        private static void ModifyExpandedNodeSegment(TreeSegment expandedNodeSegment, int nodesBeforeInsert)
        {
            expandedNodeSegment.RecordCount = nodesBeforeInsert;
            expandedNodeSegment.LastTreeRow = expandedNodeSegment.FirstTreeRow + nodesBeforeInsert - 1;
            expandedNodeSegment.LastSortID = expandedNodeSegment.FirstSortID + nodesBeforeInsert - 1;
        }
        private static async Task<(int insertedRows, int nextSegmentId, int nextPosition, int nextTreeRow)> AddStagedSegments(TreeNodeRequest request, SqlConnection conn, TreeSegment expandedNodeSegment, int insertedRows, int nextSegmentId, int nextPosition, int nextTreeRow, List<TreeSegment> stagedChildren)
        {
            using (var cmd = new SqlCommand("SELECT ParentID, StageDate, CurrentSequenceNumber FROM PerParentSequence WHERE ParentID = @ParentID ORDER BY StageDate", conn))
            {
                cmd.Parameters.AddWithValue("@ParentID", request.ID);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int count = reader.GetInt32(2);
                    stagedChildren.Add(new TreeSegment
                    {
                        SegmentID = nextSegmentId++,
                        ParentSegmentID = expandedNodeSegment.SegmentID,
                        SegmentPosition = nextPosition++,
                        ParentID = request.ID,
                        TreeLevel = request.TreeLevel + 1,
                        StageDate = DateOnly.FromDateTime(reader.GetDateTime(1)),
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

            return (insertedRows, nextSegmentId, nextPosition, nextTreeRow);
        }
        private static void AddProcessedSegment(TreeNodeRequest request, TreeSegment expandedNodeSegment, ref int nextSegmentId, ref int nextPosition, int nextTreeRow, List<TreeSegment> stagedChildren)
        {
            stagedChildren.Add(new TreeSegment
            {
                SegmentID = nextSegmentId++,
                ParentSegmentID = expandedNodeSegment.SegmentID,
                SegmentPosition = nextPosition++,
                ParentID = request.ID,
                TreeLevel = request.TreeLevel + 1,
                StageDate = new DateOnly(1900, 1, 1),
                RecordCount = request.ChildCount,
                FirstTreeRow = nextTreeRow,
                LastTreeRow = nextTreeRow + request.ChildCount - 1,
                FirstSortID = 1,
                LastSortID = request.ChildCount
            });
        }
        private static void AddSplitSegment(TreeSegment expandedNodeSegment, bool splitIsRequired, int nodesBeforeInsert, int nodesAfterInsert, ref int nextSegmentId, ref int nextPosition, ref int nextTreeRow, List<TreeSegment> stagedChildren)
        {
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
        }
        private static void ShiftSubsequentSegmentsDown(List<TreeSegment> segments, int insertedRows, int insertIndex, List<TreeSegment> stagedChildren)
        {
            for (int i = insertIndex; i < segments.Count; i++)
            {
                segments[i].SegmentPosition += stagedChildren.Count + 1;
                segments[i].FirstTreeRow += insertedRows;
                segments[i].LastTreeRow += insertedRows;
            }
        }
    }
}
