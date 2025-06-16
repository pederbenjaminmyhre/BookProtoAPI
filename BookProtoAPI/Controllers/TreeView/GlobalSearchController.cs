using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

[ApiController]
[Route("api/globalsearch")]
public class GlobalSearchController : ControllerBase
{
    private readonly string _connectionString;

    public GlobalSearchController(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")!;
    }

    [HttpPost("start")]
    public IActionResult StartJob([FromBody] List<string> searchStrings)
    {
        var (jobId, isNew) = CreateOrFindJob(searchStrings);

        if (!isNew)
        {
            return Ok(new { jobId, skipPolling = true });
        }

        _ = Task.Run(() => RunStoredProcedureAsync(jobId));
        return Ok(new { jobId, skipPolling = false });
    }

    private (int jobId, bool isNew) CreateOrFindJob(List<string> searchStrings)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand("dbo.CreateOrFindGlobalSearchJob", conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        var table = new DataTable();
        table.Columns.Add("SearchString", typeof(string));
        foreach (var str in searchStrings)
            table.Rows.Add(str);

        var param = cmd.Parameters.AddWithValue("@SearchStrings", table);
        param.SqlDbType = SqlDbType.Structured;
        param.TypeName = "dbo.SearchStringTableType";

        var jobIdOut = new SqlParameter("@JobId", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var isNewOut = new SqlParameter("@IsNew", SqlDbType.Bit) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(jobIdOut);
        cmd.Parameters.Add(isNewOut);

        conn.Open();
        cmd.ExecuteNonQuery();

        return ((int)jobIdOut.Value, (bool)isNewOut.Value);
    }

    [HttpGet("status/{jobId}")]
    public IActionResult GetStatus(int jobId)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand("dbo.GetGlobalSearchJobStatus", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@JobId", jobId);
        conn.Open();

        var status = cmd.ExecuteScalar()?.ToString();
        if (status == null) return NotFound();
        return Ok(new { jobId, status });
    }

    [HttpGet("result/{jobId}")]
    public IActionResult GetResults(int jobId)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand("dbo.GetGlobalSearchResults", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@JobId", jobId);
        conn.Open();

        var reader = cmd.ExecuteReader();
        var results = new List<object>();
        while (reader.Read())
        {
            results.Add(new
            {
                OperationsID = reader.GetInt32(0),
                ParentID = reader.GetInt32(1),
                SortID = reader.GetInt32(2),
                HasChildren = reader.GetBoolean(3),
                ChildCount = reader.GetInt32(4),
                Name = reader.GetString(5)
            });
        }

        return Ok(results);
    }

    private async Task RunStoredProcedureAsync(int jobId)
    {
        using var conn = new SqlConnection(_connectionString);
        using var cmd = new SqlCommand("dbo.LongRunningBulkInsert", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.AddWithValue("@JobId", jobId);

        await conn.OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }
}