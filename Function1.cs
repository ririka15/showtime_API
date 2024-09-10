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
using Newtonsoft.Json;
using FunctionAPIApp.Helpers;

namespace FunctionAPIApp
{
    public static class Functions1
    {
       

        [FunctionName("InsertCustomerOrder")]
        public static async Task<IActionResult> InsertCustomerOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing an insert customer order request.");

            // GETメソッド用のパラメーター取得
            string CustomerName = req.Query["CustomerName"];
            string PhoneNumber = req.Query["PhoneNumber"];
            string EmailAddress = req.Query["EmailAddress"];
            string Address = req.Query["Address"];
            string Password = req.Query["Password"];
            string OrderDateStr = req.Query["OrderDate"];
            string TotalAmountStr = req.Query["TotalAmount"];
            string[] ItemNames = req.Query["ItemName"].ToArray();
            string[] QuantityArray = req.Query["Quantity"].ToArray();

            DateTime OrderDate;
            if (!DateTime.TryParse(OrderDateStr, out OrderDate))
            {
                OrderDate = DateTime.UtcNow; // デフォルトで現在の日時を使用
            }
            else
            {
                // OrderDate の Kind プロパティを UTC に設定
                OrderDate = DateTime.SpecifyKind(OrderDate, DateTimeKind.Utc);
            }

            // JSTに変換
            TimeZoneInfo jstZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            OrderDate = TimeZoneInfo.ConvertTimeFromUtc(OrderDate, jstZone);

            decimal TotalAmount;
            if (!decimal.TryParse(TotalAmountStr, out TotalAmount))
            {
                TotalAmount = 0; // デフォルト値
            }

            // POSTメソッド用のパラメーター取得
            if (req.Method == "POST")
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                CustomerName = CustomerName ?? data?.CustomerName;
                PhoneNumber = PhoneNumber ?? data?.PhoneNumber;
                EmailAddress = EmailAddress ?? data?.EmailAddress;
                Address = Address ?? data?.Address;
                Password = Password ?? data?.Password;
                OrderDate = data?.OrderDate ?? OrderDate;
                TotalAmount = data?.TotalAmount ?? TotalAmount;
                ItemNames = data?.ItemName?.ToObject<string[]>() ?? ItemNames;
                QuantityArray = data?.Quantity?.ToObject<string[]>() ?? QuantityArray;
            }

            // 入力の検証
            if (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(PhoneNumber) || string.IsNullOrWhiteSpace(EmailAddress) || string.IsNullOrWhiteSpace(Address) || string.IsNullOrWhiteSpace(Password) || TotalAmount <= 0 || ItemNames.Length == 0 || QuantityArray.Length == 0 || ItemNames.Length != QuantityArray.Length)
            {
                return new BadRequestObjectResult(new { Message = "パラメーターが不足しています。" });
            }

            try
            {
                // パスワードをハッシュ化
                string hashedPassword = PasswordHelper.HashPassword(Password);

                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
                {
                    DataSource = "m3hhasegawafunctiondb.database.windows.net",
                    UserID = "sqladmin",
                    Password = "Showtime4",
                    InitialCatalog = "m3h-hasegawa-functionDB"
                };

                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    await connection.OpenAsync();

                    // トランザクションを開始
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 顧客情報をCustomer_tableに挿入
                            string customerSql = "INSERT INTO Customer_table (CustomerName, PhoneNumber, EmailAddress, Address, PasswordHash) OUTPUT INSERTED.CustomerID VALUES (@CustomerName, @PhoneNumber, @EmailAddress, @Address, @PasswordHash)";
                            int customerId;
                            using (SqlCommand command = new SqlCommand(customerSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerName", CustomerName);
                                command.Parameters.AddWithValue("@PhoneNumber", PhoneNumber);
                                command.Parameters.AddWithValue("@EmailAddress", EmailAddress);
                                command.Parameters.AddWithValue("@Address", Address);
                                command.Parameters.AddWithValue("@PasswordHash", hashedPassword);

                                customerId = (int)await command.ExecuteScalarAsync();
                            }

                            // 注文情報をOrder_tableに挿入
                            string orderSql = "INSERT INTO Order_table (CustomerID, OrderDate, TotalAmount) OUTPUT INSERTED.OrderID VALUES (@CustomerID, @OrderDate, @TotalAmount)";
                            int orderId;
                            using (SqlCommand command = new SqlCommand(orderSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerID", customerId);
                                command.Parameters.AddWithValue("@OrderDate", OrderDate);
                                command.Parameters.AddWithValue("@TotalAmount", TotalAmount);

                                orderId = (int)await command.ExecuteScalarAsync();
                            }

                            // 注文アイテム情報をOrderItem_tableに挿入
                            string getItemIdSql = "SELECT ItemID FROM Item_table WHERE ItemName = @ItemName";
                            string orderItemSql = "INSERT INTO OrderItem_table (OrderId, ItemID, Quantity) VALUES (@OrderId, @ItemID, @Quantity)";

                            using (SqlCommand command = new SqlCommand(getItemIdSql, connection, transaction))
                            {
                                for (int i = 0; i < ItemNames.Length; i++)
                                {
                                    command.Parameters.Clear();
                                    command.Parameters.AddWithValue("@ItemName", ItemNames[i]);

                                    // 商品IDを取得
                                    object result = await command.ExecuteScalarAsync();
                                    if (result == null)
                                    {
                                        throw new Exception($"商品名 '{ItemNames[i]}' が見つかりません。");
                                    }

                                    int itemId = Convert.ToInt32(result);

                                    // OrderItem_tableに挿入
                                    using (SqlCommand orderItemCommand = new SqlCommand(orderItemSql, connection, transaction))
                                    {
                                        orderItemCommand.Parameters.AddWithValue("@OrderId", orderId);
                                        orderItemCommand.Parameters.AddWithValue("@ItemID", itemId);
                                        orderItemCommand.Parameters.AddWithValue("@Quantity", int.Parse(QuantityArray[i]));

                                        await orderItemCommand.ExecuteNonQueryAsync();
                                    }
                                }
                                // 在庫を更新する処理を追加
                                string updateStockSql = "UPDATE Item_table SET Stock = Stock - @Quantity WHERE ItemName = @ItemName";
                                using (SqlCommand updateStockcommand = new SqlCommand(updateStockSql, connection, transaction))
                                {
                                    for (int i = 0; i < ItemNames.Length; i++)
                                    {
                                        updateStockcommand.Parameters.Clear();

                                        updateStockcommand.Parameters.AddWithValue("@ItemName", ItemNames[i]);
                                        updateStockcommand.Parameters.AddWithValue("@Quantity", QuantityArray[i]);

                                        int rowsAffected = await updateStockcommand.ExecuteNonQueryAsync();
                                        if (rowsAffected == 0)
                                        {
                                            // アイテムが見つからない場合
                                            throw new Exception($"ItemID {ItemNames[i]} not found.");
                                        }
                                    }
                                }



                            }

                            // トランザクションをコミット
                            transaction.Commit();

                            return new OkObjectResult(new { Message = "注文が正常に完了しました。" });
                        }
                        catch (Exception ex)
                        {
                            // エラーが発生した場合、トランザクションをロールバック
                            transaction.Rollback();
                            log.LogError($"エラーが発生しました: {ex.Message}");
                            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                log.LogError($"SQLエラー: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception e)
            {
                log.LogError($"一般エラー: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
