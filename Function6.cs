using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
namespace FunctionAPIApp
{
    public static class PrayerReservationFunctions
    {
        [FunctionName("PrayerReservationSELECT")]
        public static async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
    ILogger log)
        {
            var resultList = new List<object>();

            try
            {
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
                    string sql = @"
                SELECT reservationDate, reservationTime
                FROM PrayerReservation
                WHERE CustomerID = @CustomerID";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@CustomerID", customerID);
                        connection.Open();

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (reader.Read())
                            {
                                var reservationDate = reader.GetDateTime(0).ToString("yyyy-MM-dd");
                                var reservationTime = reader.GetTimeSpan(1).ToString(@"hh\:mm"); // TimeSpan を文字列に変換

                                var item = new
                                {
                                    ReservationDate = reservationDate,
                                    ReservationTime = reservationTime
                                };
                                resultList.Add(item);
                            }
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                log.LogError($"SQL Error: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                log.LogError($"General Error: {ex.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            return new OkObjectResult(resultList);
        }
    }
}