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
using System;

public static class CancelReservation
{
    [FunctionName("CancelReservation")]
    public static async Task<IActionResult> RunCancelReservation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        List<int> reservationIDs;

        if (req.Method == HttpMethods.Post)
        {
            // POSTメソッドから予約IDリストを取得
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            reservationIDs = data?.reservationIDs?.ToObject<List<int>>();
        }
        else // GETメソッド
        {
            // クエリパラメータから予約IDを取得
            string reservationIdQuery = req.Query["reservationID"];
            if (int.TryParse(reservationIdQuery, out int reservationID))
            {
                reservationIDs = new List<int> { reservationID };
            }
            else
            {
                return new BadRequestObjectResult(new { Message = "無効な予約IDです。" });
            }
        }

        if (reservationIDs == null || !reservationIDs.Any())
        {
            return new BadRequestObjectResult(new { Message = "無効な予約IDリストです。" });
        }

        try
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
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
                        // 予約IDごとに処理を実行
                        foreach (var reservationID in reservationIDs.Distinct())
                        {
                            // 予約を削除
                            string deleteReservationSql = "DELETE FROM PrayerReservation WHERE ReservationID = @ReservationID";
                            using (SqlCommand deleteReservationCommand = new SqlCommand(deleteReservationSql, connection, transaction))
                            {
                                deleteReservationCommand.Parameters.AddWithValue("@ReservationID", reservationID);
                                await deleteReservationCommand.ExecuteNonQueryAsync();
                            }
                        }

                        // トランザクションをコミット
                        transaction.Commit();

                        return new OkObjectResult(new { Message = "予約がキャンセルされました。" });
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
