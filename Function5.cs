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
using FunctionAPIApp.Helpers;

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

            // GETメソッド用のパラメーター取得
            string CustomerName = req.Query["CustomerName"];
            string PhoneNumber = req.Query["PhoneNumber"];
            string EmailAddress = req.Query["EmailAddress"];
            string Address = req.Query["Address"];
            string Password = req.Query["Password"];
            string ReservationDate = req.Query["ReservationDate"];
            string ReservationTime = req.Query["ReservationTime"];


            // POSTメソッド用のパラメーター取得
            if (req.Method == HttpMethods.Post)
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                CustomerName = CustomerName ?? data?.CustomerName;
                PhoneNumber = PhoneNumber ?? data?.PhoneNumber;
                EmailAddress = EmailAddress ?? data?.EmailAddress;
                Address = Address ?? data?.Address;
                Password = Password ?? data?.Password;
                ReservationDate = data?.ReservationDate ?? ReservationDate;
                ReservationTime = data?.ReservationTime ?? ReservationTime;
                
            }

            // 入力の検証
            if (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(PhoneNumber) || string.IsNullOrWhiteSpace(EmailAddress) || string.IsNullOrWhiteSpace(Address) || string.IsNullOrWhiteSpace(Password)  )
            {
                return new BadRequestObjectResult(new { Message = "Required fields are missing or invalid." });
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
                            // 顧客情報を Customer_table に挿入
                            string customerSql = @"
                                INSERT INTO Customer_table (CustomerName, PhoneNumber, EmailAddress, Address, PasswordHash)
                                OUTPUT INSERTED.CustomerID
                                VALUES (@CustomerName, @PhoneNumber, @EmailAddress, @Address, @PasswordHash);
                            ";
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

                            // 予約情報を PrayerReservation に挿入
                            string reservationSql = @"
                                INSERT INTO PrayerReservation (CustomerID, ReservationDate, ReservationTime)
                                OUTPUT INSERTED.ReservationID
                                VALUES (@CustomerID, @ReservationDate, @ReservationTime);
                            ";
                            int reservationId;
                            using (SqlCommand command = new SqlCommand(reservationSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerID", customerId);
                                command.Parameters.AddWithValue("@ReservationDate", ReservationDate);
                                command.Parameters.AddWithValue("@ReservationTime", ReservationTime);

                                reservationId = (int)await command.ExecuteScalarAsync();
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
