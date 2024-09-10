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

            // GET���\�b�h�p�̃p�����[�^�[�擾
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
                OrderDate = DateTime.UtcNow; // �f�t�H���g�Ō��݂̓������g�p
            }
            else
            {
                // OrderDate �� Kind �v���p�e�B�� UTC �ɐݒ�
                OrderDate = DateTime.SpecifyKind(OrderDate, DateTimeKind.Utc);
            }

            // JST�ɕϊ�
            TimeZoneInfo jstZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            OrderDate = TimeZoneInfo.ConvertTimeFromUtc(OrderDate, jstZone);

            decimal TotalAmount;
            if (!decimal.TryParse(TotalAmountStr, out TotalAmount))
            {
                TotalAmount = 0; // �f�t�H���g�l
            }

            // POST���\�b�h�p�̃p�����[�^�[�擾
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

            // ���͂̌���
            if (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(PhoneNumber) || string.IsNullOrWhiteSpace(EmailAddress) || string.IsNullOrWhiteSpace(Address) || string.IsNullOrWhiteSpace(Password) || TotalAmount <= 0 || ItemNames.Length == 0 || QuantityArray.Length == 0 || ItemNames.Length != QuantityArray.Length)
            {
                return new BadRequestObjectResult(new { Message = "�p�����[�^�[���s�����Ă��܂��B" });
            }

            try
            {
                // �p�X���[�h���n�b�V����
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

                    // �g�����U�N�V�������J�n
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // �ڋq����Customer_table�ɑ}��
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

                            // ��������Order_table�ɑ}��
                            string orderSql = "INSERT INTO Order_table (CustomerID, OrderDate, TotalAmount) OUTPUT INSERTED.OrderID VALUES (@CustomerID, @OrderDate, @TotalAmount)";
                            int orderId;
                            using (SqlCommand command = new SqlCommand(orderSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@CustomerID", customerId);
                                command.Parameters.AddWithValue("@OrderDate", OrderDate);
                                command.Parameters.AddWithValue("@TotalAmount", TotalAmount);

                                orderId = (int)await command.ExecuteScalarAsync();
                            }

                            // �����A�C�e������OrderItem_table�ɑ}��
                            string getItemIdSql = "SELECT ItemID FROM Item_table WHERE ItemName = @ItemName";
                            string orderItemSql = "INSERT INTO OrderItem_table (OrderId, ItemID, Quantity) VALUES (@OrderId, @ItemID, @Quantity)";

                            using (SqlCommand command = new SqlCommand(getItemIdSql, connection, transaction))
                            {
                                for (int i = 0; i < ItemNames.Length; i++)
                                {
                                    command.Parameters.Clear();
                                    command.Parameters.AddWithValue("@ItemName", ItemNames[i]);

                                    // ���iID���擾
                                    object result = await command.ExecuteScalarAsync();
                                    if (result == null)
                                    {
                                        throw new Exception($"���i�� '{ItemNames[i]}' ��������܂���B");
                                    }

                                    int itemId = Convert.ToInt32(result);

                                    // OrderItem_table�ɑ}��
                                    using (SqlCommand orderItemCommand = new SqlCommand(orderItemSql, connection, transaction))
                                    {
                                        orderItemCommand.Parameters.AddWithValue("@OrderId", orderId);
                                        orderItemCommand.Parameters.AddWithValue("@ItemID", itemId);
                                        orderItemCommand.Parameters.AddWithValue("@Quantity", int.Parse(QuantityArray[i]));

                                        await orderItemCommand.ExecuteNonQueryAsync();
                                    }
                                }
                                // �݌ɂ��X�V���鏈����ǉ�
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
                                            // �A�C�e����������Ȃ��ꍇ
                                            throw new Exception($"ItemID {ItemNames[i]} not found.");
                                        }
                                    }
                                }



                            }

                            // �g�����U�N�V�������R�~�b�g
                            transaction.Commit();

                            return new OkObjectResult(new { Message = "����������Ɋ������܂����B" });
                        }
                        catch (Exception ex)
                        {
                            // �G���[�����������ꍇ�A�g�����U�N�V���������[���o�b�N
                            transaction.Rollback();
                            log.LogError($"�G���[���������܂���: {ex.Message}");
                            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                log.LogError($"SQL�G���[: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception e)
            {
                log.LogError($"��ʃG���[: {e.Message}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
