using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

public static class GetProductsFunction
{
    [FunctionName("GetProducts")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function to get all products.");

        try
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = "m3hhasegawafunctiondb.database.windows.net",
                UserID = "sqladmin",
                Password = "Showtime4",
                InitialCatalog = "m3h-hasegawa-functionDB"
            };

            List<object> productList = new List<object>();

            using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();
                string selectSql = "SELECT ItemID, ItemName, Price, Stock FROM Item_table"; 
                using (SqlCommand command = new SqlCommand(selectSql, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            productList.Add(new
                            {
                                id = (int)reader["ItemID"],
                                name = reader["ItemName"].ToString(),
                                price = (int)reader["Price"],
                                stock = (int)reader["Stock"],
                            });
                        }
                    }
                }
            }

            return new OkObjectResult(productList);
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
