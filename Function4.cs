using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace FunctionAPICustomerDelete
{
    public static class DeleteCustomerFunction
    {
        [FunctionName("DeleteCustomer")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // クエリパラメータからCustomerIDを取得
            string customerID = req.Query["CustomerID"];

            if (string.IsNullOrEmpty(customerID))
            {
                return new BadRequestObjectResult(new { message = "CustomerID is required." });
            }

            // SqlConnectionStringBuilderを使用して接続文字列を構築
            string connectionString;
            try
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    DataSource = "m3hhasegawafunctiondb.database.windows.net",
                    UserID = "sqladmin",
                    Password = "Showtime4",
                    InitialCatalog = "m3h-hasegawa-functionDB"
                };

                connectionString = builder.ToString();
            }
            catch (Exception ex)
            {
                log.LogError($"Error constructing connection string: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // 顧客を削除するSQLクエリ
                    string query = "DELETE FROM Customer_table WHERE CustomerID = @CustomerID";

                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@CustomerID", customerID);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            return new OkObjectResult(new { message = "Customer deleted successfully." });
                        }
                        else
                        {
                            return new NotFoundObjectResult(new { message = "Customer not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError($"Error deleting customer: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
