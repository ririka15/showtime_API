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
using System.Linq;

namespace FunctionAPIApp
{
    public static class Functions1
    {
        [FunctionName("InsertOrder")]
        public static async Task<IActionResult> InsertOrder(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing an insert order request.");

            // GET メソッドでのパラメータ取得
            string CustomerID = req.Query["CustomerID"];
            string OrderDateStr = req.Query["OrderDate"];
            string TotalAmountStr = req.Query["TotalAmount"];
            string[] ItemNames = req.Query["ItemName"].ToArray();
            string[] QuantityArray = req.Query["Quantity"].ToArray();

            // POST メソッドでのパラメータ取得
            if (req.Method == "POST")
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                CustomerID = CustomerID ?? data?.CustomerID;
                OrderDateStr = OrderDateStr ?? data?.OrderDate;
                TotalAmountStr = TotalAmountStr ?? data?.TotalAmount;
                ItemNames = ItemNames.Length > 0 ? ItemNames : data?.ItemName?.ToObject<string[]>() ?? Array.Empty<string>();
                QuantityArray = QuantityArray.Length > 0 ? QuantityArray : data?.Quantity?.ToObject<string[]>() ?? Array.Empty<string>();
            }

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

            // リクエスト内容をログに出力
            log.LogInformation($"Request Body: {await new StreamReader(req.Body).ReadToEndAsync()}");
            log.LogInformation($"CustomerID: {CustomerID}");
            log.LogInformation($"OrderDateStr: {OrderDateStr}");
            log.LogInformation($"TotalAmountStr: {TotalAmountStr}");
            log.LogInformation($"ItemNames: {string.Join(", ", ItemNames)}");
            log.LogInformation($"QuantityArray: {string.Join(", ", QuantityArray)}");

            // 入力の検証
            List<string> errors = new List<string>();
            if (string.IsNullOrEmpty(CustomerID))
            {
                errors.Add("Error: CustomerID is missing");
            }
            if (TotalAmount <= 0)
            {
                errors.Add("Error: TotalAmount is invalid");
            }
            if (ItemNames.Length == 0)
            {
                errors.Add("Error: ItemNames is empty");
            }
            if (QuantityArray.Length == 0)
            {
                errors.Add("Error: QuantityArray is empty");
            }
            if (ItemNames.Length != QuantityArray.Length)
            {
                errors.Add("Error: ItemNames and QuantityArray lengths do not match");
            }

            if (errors.Any())
            {
                log.LogInformation(string.Join("; ", errors));
                return new BadRequestObjectResult(new { Message = "パラメーターが不足しています。" });
            }

            try
            {
                // DB接続設定
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
                            // 注文情報をOrder_tableに挿入
                            string orderSql = "INSERT INTO Order_table (CustomerID, OrderDate, TotalAmount) OUTPUT INSERTED.OrderID VALUES (@CustomerID, @OrderDate, @TotalAmount)";
                            int orderId;
                            using (SqlCommand command = new SqlCommand(orderSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerID", CustomerID);
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
