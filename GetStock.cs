using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace FunctionAPIApp
{
    public static class GetAllStockFunction
    {
        [FunctionName("GetAllStock")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function to get all stock.");

            try
            {
                // SQL 接続文字列の設定
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    DataSource = "m3hhasegawafunctiondb.database.windows.net",
                    UserID = "sqladmin",
                    Password = "Showtime4",
                    InitialCatalog = "m3h-hasegawa-functionDB"
                };

                // 結果を格納するリスト
                List<object> stockList = new List<object>();

                // データベース接続を開き、すべての Stock を取得
                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();

                    string selectSql = "SELECT ItemID, Stock FROM Item_table";
                    using (SqlCommand command = new SqlCommand(selectSql, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                stockList.Add(new
                                {
                                    ItemID = reader["ItemID"].ToString(),
                                    Stock = (int)reader["Stock"]
                                });
                            }
                        }
                    }
                }

                // 取得したすべての Stock の値を JSON 形式で返す
                return new OkObjectResult(stockList);
            }
            catch (SqlException e)
            {
                log.LogError($"SQL エラー: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception e)
            {
                log.LogError($"エラー: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
