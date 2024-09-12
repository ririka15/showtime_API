using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Data.SqlClient;
using Microsoft.Data.SqlClient;


public static class CancelOrder
{
    [FunctionName("CancelOrder")]
    public static async Task<IActionResult> RunCancelOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get" ,"post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        // リクエストボディから注文IDリストを取得
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        List<int> orderIDs = data?.orderIDs?.ToObject<List<int>>();

        if (orderIDs == null || !orderIDs.Any())
        {
            return new BadRequestObjectResult(new { Message = "無効な注文IDリストです。" });
        }

        try
        {
            SqlConnectionStringBuilder  builder = new SqlConnectionStringBuilder
            {
                DataSource = "m3hhasegawafunctiondb.database.windows.net",
                UserID = "sqladmin",
                Password = "Showtime4",
                InitialCatalog = "m3h-hasegawa-functionDB",
                MultipleActiveResultSets = false // MARSを無効化
            };

            using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();

                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 注文IDごとに処理を実行
                        foreach (var orderID in orderIDs.Distinct())
                        {
                            // 注文アイテムと数量を保存するためのリスト
                            var orderItems = new List<(string ItemID, int Quantity)>();

                            // 注文アイテムの取得
                            string getOrderItemsSql = "SELECT ItemID, Quantity FROM OrderItem_table WHERE OrderID = @OrderID";
                            using (SqlCommand getOrderItemsCommand = new SqlCommand(getOrderItemsSql, connection, transaction))
                            {
                                getOrderItemsCommand.Parameters.AddWithValue("@OrderID", orderID);

                                using (SqlDataReader reader = await getOrderItemsCommand.ExecuteReaderAsync())
                                {
                                    while (await reader.ReadAsync())
                                    {
                                        string itemId = reader["ItemID"].ToString();
                                        int quantity = (int)reader["Quantity"];
                                        orderItems.Add((itemId, quantity));
                                    }
                                }
                            }

                            // OrderItem_tableから注文アイテムを削除
                            string deleteOrderItemsSql = "DELETE FROM OrderItem_table WHERE OrderID = @OrderID";
                            using (SqlCommand deleteOrderItemsCommand = new SqlCommand(deleteOrderItemsSql, connection, transaction))
                            {
                                deleteOrderItemsCommand.Parameters.AddWithValue("@OrderID", orderID);
                                await deleteOrderItemsCommand.ExecuteNonQueryAsync();
                            }

                            // Order_tableから注文を削除
                            string deleteOrderSql = "DELETE FROM Order_table WHERE OrderID = @OrderID";
                            using (SqlCommand deleteOrderCommand = new SqlCommand(deleteOrderSql, connection, transaction))
                            {
                                deleteOrderCommand.Parameters.AddWithValue("@OrderID", orderID);
                                await deleteOrderCommand.ExecuteNonQueryAsync();
                            }

                            // 在庫を更新（SqlDataReaderを閉じた後で実行）
                            foreach (var (itemId, quantity) in orderItems)
                            {
                                string updateStockSql = "UPDATE Item_table SET Stock = Stock + @Quantity WHERE ItemID = @ItemID";
                                using (SqlCommand updateStockCommand = new SqlCommand(updateStockSql, connection, transaction))
                                {
                                    updateStockCommand.Parameters.AddWithValue("@ItemID", itemId);
                                    updateStockCommand.Parameters.AddWithValue("@Quantity", quantity);

                                    await updateStockCommand.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // トランザクションをコミット
                        transaction.Commit();

                        return new OkObjectResult(new { Message = "指定された全ての注文が正常にキャンセルされ、在庫が更新されました。" });
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