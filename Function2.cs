using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using FunctionAPIApp.Helpers;

namespace FunctionAPIApp
{
    public static class Function2
    {
        [FunctionName("OrderSELECT")]
        public static async Task<IActionResult> OrderSELECT(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var resultList = new List<object>();

            try
            {
                // クエリパラメータからCustomerIDを取得
                string customerID = req.Query["CustomerID"];

                // POSTメソッドでのCustomerID取得
                if (req.Method == "POST")
                {
                    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    dynamic data = JsonConvert.DeserializeObject(requestBody);
                    customerID = customerID ?? data?.CustomerID;
                }

                if (string.IsNullOrEmpty(customerID))
                {
                    return new BadRequestObjectResult("CustomerID is required.");
                }

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
                        SELECT o.OrderID, i.ItemName AS ItemName, oi.Quantity, o.OrderDate, o.TotalAmount
                        FROM Order_table o
                        JOIN OrderItem_table oi ON o.OrderID = oi.OrderID
                        JOIN Item_table i ON oi.ItemID = i.ItemID
                        WHERE o.CustomerID = @CustomerID";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        // パラメータにCustomerIDを設定
                        command.Parameters.AddWithValue("@CustomerID", customerID);

                        connection.Open();

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (reader.Read())
                            {
                                var item = new
                                {
                                    OrderID = reader.GetInt32(0), // OrderIDを追加
                                    ItemName = reader.GetString(1),
                                    Quantity = reader.GetInt32(2),
                                    OrderDate = reader.GetDateTime(3),
                                    TotalAmount = reader.GetDecimal(4)
                                };
                                resultList.Add(item);
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

            return new OkObjectResult(resultList);
        }
    }
}
