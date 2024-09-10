using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System;

namespace FunctionAPIApp
{
    public static class CustomerProfileFunction
    {
        [FunctionName("CustomerProfile")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "CustomerProfile")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing a customer profile request.");

            // クエリパラメータからCustomerIDを取得
            string customerID = req.Query["CustomerID"];

            if (string.IsNullOrEmpty(customerID))
            {
                return new BadRequestObjectResult("CustomerID is required.");
            }

            try
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    DataSource = "m3hhasegawafunctiondb.database.windows.net",
                    UserID = "sqladmin",
                    Password = "Showtime4",
                    InitialCatalog = "m3h-hasegawa-functionDB"
                };

                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    // SQLクエリをCustomerIDでフィルタリング
                    string sql = @"
                        SELECT CustomerName AS Name, PhoneNumber AS Phone, EmailAddress AS Email, Address
                        FROM Customer_table
                        WHERE CustomerID = @CustomerID";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@CustomerID", customerID);

                        connection.Open();

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var customerData = new
                                {
                                    Name = reader.GetString(0),
                                    Phone = reader.GetString(1),
                                    Email = reader.GetString(2),
                                    Address = reader.GetString(3)
                                };

                                return new OkObjectResult(customerData);
                            }
                            else
                            {
                                return new NotFoundObjectResult("Customer not found.");
                            }
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                log.LogError($"SQL Error: {e.ToString()}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                log.LogError($"General Error: {ex.ToString()}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
