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

namespace FunctionAPIApp
{
    public static class InsertPrayerReservationFunction
    {
        [FunctionName("InsertPrayerReservation")]
        public static async Task<IActionResult> InsertPrayerReservation(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Processing an insert prayer reservation request.");
            int CustomerID;
            string ReservationDate;
            string ReservationTime;

            // GET メソッド用のパラメーター取得
            if (req.Method == HttpMethods.Get)
            {
                CustomerID = int.TryParse(req.Query["CustomerID"], out var id) ? id : 0;
                ReservationDate = req.Query["ReservationDate"];
                ReservationTime = req.Query["ReservationTime"];
            }
            // POST メソッド用のパラメーター取得
            else
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                CustomerID = data?.CustomerID ?? 0;
                ReservationDate = data?.ReservationDate;
                ReservationTime = data?.ReservationTime;
            }


            // 入力の検証
            if (CustomerID <= 0 || string.IsNullOrWhiteSpace(ReservationDate) || string.IsNullOrWhiteSpace(ReservationTime))
            {
                return new BadRequestObjectResult(new { Message = "Required fields are missing or invalid." });
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
                    await connection.OpenAsync();

                    // トランザクションを開始
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 予約情報を PrayerReservation に挿入
                            string reservationSql = @"
                                INSERT INTO PrayerReservation (CustomerID, ReservationDate, ReservationTime)
                                OUTPUT INSERTED.ReservationID
                                VALUES (@CustomerID, @ReservationDate, @ReservationTime);
                            ";
                            int ReservationID;
                            using (SqlCommand command = new SqlCommand(reservationSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerID", CustomerID);
                                command.Parameters.AddWithValue("@ReservationDate", ReservationDate);
                                command.Parameters.AddWithValue("@ReservationTime", ReservationTime);

                                ReservationID = (int)await command.ExecuteScalarAsync();
                            }

                            // トランザクションをコミット
                            transaction.Commit();

                            return new OkObjectResult(new { Message = "Reservation completed successfully." });
                        }
                        catch (Exception ex)
                        {
                            // エラー発生時はトランザクションをロールバック
                            transaction.Rollback();
                            log.LogError($"Error inserting reservation: {ex.Message}");
                            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                log.LogError($"SQL error: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception e)
            {
                log.LogError($"General error: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
