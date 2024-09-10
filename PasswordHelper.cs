using BCrypt.Net;

namespace FunctionAPIApp.Helpers
{
    public static class PasswordHelper
    {
        // パスワードをハッシュ化するメソッド
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        // ハッシュ化されたパスワードと入力されたパスワードを比較するメソッド
        public static bool VerifyPassword(string password, string hashedPassword)
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}
