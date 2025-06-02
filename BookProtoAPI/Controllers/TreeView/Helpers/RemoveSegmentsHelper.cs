using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BookProtoAPI.Controllers.TreeView.DTOs;
using BookProtoAPI.Controllers.TreeView.Models;

namespace BookProtoAPI.Controllers.TreeView.Helpers
{
    public static class RemoveSegmentsHelper
    {
        public static IActionResult? RemoveSegments(TreeNodeRequest request, List<TreeSegment> segments, out int deletedRows)
        {
            // Get the Collapsed Segment
            TreeSegment? collapsedSegment = GetCollapsedNodeSegment(request, segments);

            // Validate
            if (collapsedSegment == null)
            {
                deletedRows = 0;
                return new BadRequestObjectResult("Collapsed node does not match any known segment.");
            }

            // Is Merge Required?
            bool mergeIsRequired = IsMergeRequired(request, segments, collapsedSegment);

            // Declarations
            deletedRows = 0;
            int deletedSegments = 0;

            // Get Collapse location
            int collapsedIndex = segments.IndexOf(collapsedSegment);
            TreeSegment? splitSegment = null;

            // Merge values from splitSegment to collapsedSegment
            if (mergeIsRequired)
            {
                int splitSegmentIndex = GetSplitSegmentIndex(segments, collapsedSegment);

                if (splitSegmentIndex >= 0)
                {
                    splitSegment = ModifyCollapsedNodeSegment(segments, collapsedSegment, splitSegmentIndex);

                    //segments.RemoveAt(splitSegmentIndex);
                    //deletedSegments++;
                }
            }

            // Collect segments to remove
            List<TreeSegment> segmentsToDelete = GatherSegmentsForRemoval(segments, ref deletedRows, collapsedSegment);

            // add splitSegment to the end of segmentsToDelete
            if (splitSegment != null)
            {
                segmentsToDelete.Add(splitSegment);
            }

            // Count deleted segments
            deletedSegments += segmentsToDelete.Count;

            // Remove segments
            foreach (var seg in segmentsToDelete)
                segments.Remove(seg);

            // Shift Segments Up
            ShiftSubsequentSegmentsUp(segments, deletedRows, deletedSegments, collapsedIndex);

            // All code paths assign deletedRows and return a value
            return null;
        }
        private static TreeSegment? GetCollapsedNodeSegment(TreeNodeRequest request, List<TreeSegment> segments)
        {
            return segments.Find((Predicate<TreeSegment>)(s =>
                s.ParentID == request.ParentID &&
                s.StageDate == request.StageDate &&
                request.SortID >= s.FirstSortID &&
                request.SortID <= s.LastSortID));
        }
        private static bool IsMergeRequired(TreeNodeRequest request, List<TreeSegment> segments, TreeSegment collapsedSegment)
        {
            var siblings = segments.FindAll(s => s.ParentID == collapsedSegment.ParentID);
            var lastSibling = siblings[^1];
            bool isLastNode = lastSibling.SegmentID == collapsedSegment.SegmentID &&
                              request.SortID == collapsedSegment.LastSortID;
            bool mergeIsRequired = !isLastNode;
            return mergeIsRequired;
        }
        private static int GetSplitSegmentIndex(List<TreeSegment> segments, TreeSegment collapsedSegment)
        {
            return segments.FindIndex(s =>
                s.SegmentPosition > collapsedSegment.SegmentPosition &&
                s.ParentID == collapsedSegment.ParentID);
        }
        private static TreeSegment ModifyCollapsedNodeSegment(List<TreeSegment> segments, TreeSegment collapsedSegment, int splitSegmentIndex)
        {
            TreeSegment splitSegment = segments[splitSegmentIndex];
            collapsedSegment.RecordCount += splitSegment.RecordCount;
            collapsedSegment.LastTreeRow = collapsedSegment.FirstTreeRow + collapsedSegment.RecordCount - 1;
            collapsedSegment.LastSortID = collapsedSegment.FirstSortID + collapsedSegment.RecordCount - 1;
            return splitSegment;
        }
        private static List<TreeSegment> GatherSegmentsForRemoval(List<TreeSegment> segments, ref int deletedRows, TreeSegment collapsedSegment)
        {
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

            return segmentsToDelete;
        }
        private static void ShiftSubsequentSegmentsUp(List<TreeSegment> segments, int deletedRows, int deletedSegments, int collapsedIndex)
        {
            for (int i = collapsedIndex + 1; i < segments.Count; i++)
            {
                segments[i].SegmentPosition -= deletedSegments;
                segments[i].FirstTreeRow -= deletedRows;
                segments[i].LastTreeRow -= deletedRows;
            }
        }

    }
}
