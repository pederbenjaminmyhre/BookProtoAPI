using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using BookProtoAPI.Controllers.TreeView.DTOs;
using BookProtoAPI.Controllers.TreeView.Models;

namespace BookProtoAPI.Controllers.TreeView.Services
{
    public class ReportService
    {
        public async Task<List<TreeNodeResult>> GetTreeViewResults(
            SqlConnection conn,
            List<TreeSegment> segments,
            TreeViewRequest request)
        {
            int lastVisibleRow = request.FirstVisibleRow + request.RowsPerViewport - 1;

            // Get Intersecting Segments
            List<TreeSegment> intersecting = GetIntersectingSegments(segments, request, lastVisibleRow);

            // Apply Offsets
            ApplyOffsets(request, lastVisibleRow, intersecting);

            // Create Table Parameter
            DataTable queryList = CreateTableParameter();

            // Fill Table Parameter
            FillTableParameter(intersecting, queryList);

            // Call Report Procedure
            List<TreeNodeResult> results = await CallReportProcedure(conn, request, queryList);

            return results;
        }
        private static List<TreeSegment> GetIntersectingSegments(List<TreeSegment> segments, TreeViewRequest request, int lastVisibleRow)
        {
            return segments
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
        }
        private static void ApplyOffsets(TreeViewRequest request, int lastVisibleRow, List<TreeSegment> intersecting)
        {
            if (intersecting.Count > 0)
            {
                var first = intersecting[0];
                int offsetFirst = first.FirstTreeRow - first.FirstSortID;

                var last = intersecting[^1];
                //int offsetLast = last.LastTreeRow - last.FirstSortID;
                int offsetLast = last.FirstTreeRow - last.FirstSortID;

                first.FirstSortID = request.FirstVisibleRow - offsetFirst;
                last.LastSortID = lastVisibleRow - offsetLast;
            }
        }
        private static DataTable CreateTableParameter()
        {
            var queryList = new DataTable();
            queryList.Columns.Add("RowNumber", typeof(int));
            queryList.Columns.Add("StageDate", typeof(DateOnly));
            queryList.Columns.Add("ParentID", typeof(int));
            queryList.Columns.Add("FirstSortID", typeof(int));
            queryList.Columns.Add("LastSortID", typeof(int));
            queryList.Columns.Add("TreeLevel", typeof(int));
            return queryList;
        }
        private static void FillTableParameter(List<TreeSegment> intersecting, DataTable queryList)
        {
            int rowNum = 1;
            foreach (var seg in intersecting)
            {
                queryList.Rows.Add(rowNum++, seg.StageDate, seg.ParentID, seg.FirstSortID, seg.LastSortID, seg.TreeLevel);
            }
        }
        private static async Task<List<TreeNodeResult>> CallReportProcedure(SqlConnection conn, TreeViewRequest request, DataTable queryList)
        {
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
                        StageDate = DateOnly.FromDateTime(reader.GetDateTime(6)),
                        IsExpanded = reader.GetBoolean(7),
                        Name = reader.GetString(8)
                    });
                }
            }

            return results;
        }

    }
}
