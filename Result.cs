using System;

namespace ImagingTools
{
    public class Result
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public static Result OK => new Result() { Success = true, Message = "" };

        public static Result NotOk(string reason)
        {
            return new Result()
            {
                Success = false,
                Message = reason
            };
        }

        public static Result FromException(Exception ex) => NotOk(string.Join("\n", ex.Message, ex.StackTrace));
    }
}
