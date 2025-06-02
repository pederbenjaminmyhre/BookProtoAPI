using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using BookProtoAPI.Controllers.TreeView.DTOs;

namespace BookProtoAPI.Controllers.TreeView.Services
{
    public class CrudService
    {
        public async Task<(int GeneratedID, int GeneratedSortID)> Insert(SqlConnection conn, InsertedRecord record)
        {
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

            return ((int)idParam.Value, (int)sortIdParam.Value);
        }

        public async Task Update(SqlConnection conn, UpdatedRecord record)
        {
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
        }

        public async Task Delete(SqlConnection conn, DeletedRecord record)
        {
            using var cmd = new SqlCommand("dbo.DeleteTreeNode", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            cmd.Parameters.AddWithValue("@ID", record.ID);
            cmd.Parameters.AddWithValue("@StageDate", record.StageDate);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
