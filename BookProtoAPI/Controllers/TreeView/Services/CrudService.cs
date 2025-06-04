using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using BookProtoAPI.Controllers.TreeView.DTOs;

namespace BookProtoAPI.Controllers.TreeView.Services
{
    public class CrudService
    {
        public async Task<NodeRecord> Insert(SqlConnection conn, NodeRecord record)
        {
            using var cmd = new SqlCommand("dbo.InsertTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ParentID", record.parentId);
            cmd.Parameters.AddWithValue("@HasChildren", record.hasChildren);
            cmd.Parameters.AddWithValue("@ChildCount", record.childCount);
            cmd.Parameters.AddWithValue("@Name", record.name);
            cmd.Parameters.AddWithValue("@StageDate", record.stageDate);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new NodeRecord
                {
                    id = reader.GetInt32(reader.GetOrdinal("ID")),
                    parentId = reader.GetInt32(reader.GetOrdinal("ParentID")),
                    hasChildren = reader.GetBoolean(reader.GetOrdinal("HasChildren")),
                    childCount = reader.GetInt32(reader.GetOrdinal("ChildCount")),
                    name = reader.GetString(reader.GetOrdinal("Name")),
                    stageDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("StageDate"))),
                    sortId = reader.GetInt32(reader.GetOrdinal("SortID"))
                };
            }
            else
            {
                throw new Exception("InsertTreeNode did not return a result row.");
            }
        }


        public async Task<NodeRecord> Update(SqlConnection conn, NodeRecord record)
        {
            using var cmd = new SqlCommand("dbo.UpdateTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ID", record.id);
            cmd.Parameters.AddWithValue("@NewParentID", record.parentId);
            cmd.Parameters.AddWithValue("@HasChildren", record.hasChildren);
            cmd.Parameters.AddWithValue("@ChildCount", record.childCount);
            cmd.Parameters.AddWithValue("@Name", record.name);
            cmd.Parameters.AddWithValue("@StageDate", record.stageDate);

            using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new NodeRecord
                {
                    id = reader.GetInt32(reader.GetOrdinal("ID")),
                    parentId = reader.GetInt32(reader.GetOrdinal("ParentID")),
                    sortId = reader.GetInt32(reader.GetOrdinal("SortID")),
                    hasChildren = reader.GetBoolean(reader.GetOrdinal("HasChildren")),
                    childCount = reader.GetInt32(reader.GetOrdinal("ChildCount")),
                    name = reader.GetString(reader.GetOrdinal("Name")),
                    stageDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("StageDate")))
                };
            }

            throw new Exception("UpdateTreeNode did not return an updated row.");

        }


        public async Task<NodeRecord> Delete(SqlConnection conn, NodeRecord record)
        {
            try
            {
                using var cmd = new SqlCommand("dbo.DeleteTreeNode", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@ID", record.id);
                cmd.Parameters.AddWithValue("@StageDate", record.stageDate);

                await cmd.ExecuteNonQueryAsync();

                // Return the same record that was passed in
                return record;
            }
            catch (SqlException sqlEx)
            {
                throw new Exception($"Database error during deletion: {sqlEx.Message}", sqlEx);
            }
            catch (Exception ex)
            {
                throw new Exception($"Unexpected error during deletion: {ex.Message}", ex);
            }
        }
    }
}
