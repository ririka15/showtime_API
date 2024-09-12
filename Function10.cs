using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient; // Microsoft.Data.SqlClient を使用
using Newtonsoft.Json;

namespace FunctionApp
{
    public static class CustomerFunction2
    {
        [FunctionName("Customer2")]
        public static async Task<IActionResult> Run(
     [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "customer2")] HttpRequest req,
     ILogger log)
        {
            string method = req.Method.ToUpper();

            if (method == "GET")
            {
                return await GetCustomer(req, log);
            }
            else if (method == "POST")
            {
                return await UpdateCustomer(req, log);
            }
            else
            {
                return new BadRequestObjectResult("Unsupported HTTP method.");
            }
        }

        private static async Task<IActionResult> GetCustomer(HttpRequest req, ILogger log)
        {
            log.LogInformation("GetCustomer request received.");

            // クエリパラメータからCustomerIDを取得
            string customerID = req.Query["CustomerID"];

            if (string.IsNullOrEmpty(customerID))
            {
                return new BadRequestObjectResult("CustomerID is required.");
            }

            // SqlConnectionStringBuilderを使って接続文字列を構築
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = "m3hhasegawafunctiondb.database.windows.net",
                UserID = "sqladmin",
                Password = "Showtime4",
                InitialCatalog = "m3h-hasegawa-functionDB"
            };

            string connectionString = builder.ToString();

            // データベース接続とデータ取得処理
            using (var conn = new SqlConnection(connectionString))
            {
                try
                {
                    await conn.OpenAsync();
                    var query = "SELECT CustomerName, PhoneNumber, EmailAddress, Address FROM Customer_table WHERE CustomerID = @CustomerID";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@CustomerID", customerID);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var customerName = reader["CustomerName"].ToString();
                                var phoneNumber = reader["PhoneNumber"].ToString();
                                var emailAddress = reader["EmailAddress"].ToString();
                                var address = reader["Address"].ToString();
                                return new OkObjectResult(new { CustomerName = customerName, PhoneNumber = phoneNumber, EmailAddress = emailAddress, Address = address });
                            }
                            else
                            {
                                return new NotFoundObjectResult("Customer not found.");
                            }
                        }
                    }
                }
                catch (SqlException ex)
                {
                    log.LogError($"Database operation failed: {ex.Message}");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
        }

        private static async Task<IActionResult> UpdateCustomer(HttpRequest req, ILogger log)
        {
            log.LogInformation("UpdateCustomer request received.");

            // リクエストボディからCustomerIDと新しいCustomerName、PhoneNumber、EmailAddress、Addressを取得
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<UpdateCustomerRequest>(requestBody);

            if (data == null || data.CustomerID == 0 || string.IsNullOrEmpty(data.CustomerName) || string.IsNullOrEmpty(data.PhoneNumber) || string.IsNullOrEmpty(data.EmailAddress) || string.IsNullOrEmpty(data.Address))
            {
                return new BadRequestObjectResult("Invalid request. CustomerID, CustomerName, PhoneNumber, EmailAddress, and Address are required.");
            }

            // SqlConnectionStringBuilderを使って接続文字列を構築
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = "m3hhasegawafunctiondb.database.windows.net",
                UserID = "sqladmin",
                Password = "Showtime4",
                InitialCatalog = "m3h-hasegawa-functionDB"
            };

            string connectionString = builder.ToString();

            // データベース接続と更新処理
            using (var conn = new SqlConnection(connectionString))
            {
                try
                {
                    await conn.OpenAsync();
                    var query = "UPDATE Customer_table SET CustomerName = @CustomerName, PhoneNumber = @PhoneNumber, EmailAddress = @EmailAddress, Address = @Address WHERE CustomerID = @CustomerID";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@CustomerName", data.CustomerName);
                        cmd.Parameters.AddWithValue("@PhoneNumber", data.PhoneNumber);
                        cmd.Parameters.AddWithValue("@EmailAddress", data.EmailAddress);
                        cmd.Parameters.AddWithValue("@Address", data.Address);
                        cmd.Parameters.AddWithValue("@CustomerID", data.CustomerID);

                        int rowsAffected = await cmd.ExecuteNonQueryAsync();
                        if (rowsAffected == 0)
                        {
                            return new NotFoundObjectResult("Customer not found.");
                        }
                    }
                }
                catch (SqlException ex)
                {
                    log.LogError($"Database operation failed: {ex.Message}");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }

            return new NoContentResult();
        }

        public class UpdateCustomerRequest
        {
            public int CustomerID { get; set; }
            public string CustomerName { get; set; }
            public string PhoneNumber { get; set; }
            public string EmailAddress { get; set; }
            public string Address { get; set; }
        }
    }
}