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
    public static class Functions2
    {
        [FunctionName("Login")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing a login request.");

            // GETメソッド用のパラメーター取得
            string emailAddress = req.Query["EmailAddress"];
            string password = req.Query["Password"];

            // POSTメソッド用のパラメーター取得
            if (req.Method == "POST")
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                emailAddress = emailAddress ?? data?.EmailAddress;
                password = password ?? data?.Password;
            }

            // 必要なパラメーターが揃っているか確認
            if (string.IsNullOrWhiteSpace(emailAddress) || string.IsNullOrWhiteSpace(password))
            {
                return new BadRequestObjectResult(new
                {
                    Message = "EmailAddress または Password が指定されていません。",
                    StatusCode = StatusCodes.Status400BadRequest
                });
            }

            try
            {
                // DB接続設定（接続文字列の構築）
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    DataSource = "m3hhasegawafunctiondb.database.windows.net",
                    UserID = "sqladmin",
                    Password = "Showtime4",
                    InitialCatalog = "m3h-hasegawa-functionDB"
                };

                // 接続用オブジェクトの初期化
                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    // 実行するクエリ（パラメーター化）
                    string sql = "SELECT CustomerID, PasswordHash FROM Customer_table WHERE EmailAddress = @EmailAddress";

                    // SQL実行オブジェクトの初期化
                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        // パラメーターの追加
                        command.Parameters.AddWithValue("@EmailAddress", emailAddress);

                        // DBと接続
                        await connection.OpenAsync(); // 非同期メソッドで接続を開く

                        // SQLを実行し、結果を取得
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                string storedHashedPassword = reader["PasswordHash"].ToString();
                                int customerId = (int)reader["CustomerID"];

                                // パスワードを検証
                                if (PasswordHelper.VerifyPassword(password, storedHashedPassword))
                                {
                                    // メールアドレスとパスワードが一致する場合
                                    return new OkObjectResult(new
                                    {
                                        Message = "ログイン成功",
                                        StatusCode = StatusCodes.Status200OK,
                                        CustomerID = customerId // CustomerIDを含む
                                    });
                                }
                                else
                                {
                                    // パスワードが一致しない場合
                                    return new UnauthorizedObjectResult(new
                                    {
                                        Message = "EmailAddress または Password が一致しません。",
                                        StatusCode = StatusCodes.Status401Unauthorized
                                    });
                                }
                            }
                            else
                            {
                                // メールアドレスが一致しない場合
                                return new UnauthorizedObjectResult(new
                                {
                                    Message = "EmailAddress または Password が一致しません。",
                                    StatusCode = StatusCodes.Status401Unauthorized
                                });
                            }
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                // エラーをログに出力
                log.LogError(e.ToString());
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

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
