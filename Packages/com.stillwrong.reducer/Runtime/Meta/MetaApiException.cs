using System;

namespace StillWrong.Meta
{
    public sealed class MetaApiException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }

        public MetaApiException(int statusCode, string responseBody)
            : base("Meta API request failed with status " + statusCode + ".")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }

    public sealed class MissingApiKeyException : Exception
    {
        public MissingApiKeyException()
            : base("MODEL_API_KEY is not set in the environment.")
        {
        }
    }

    public sealed class SamPromptException : Exception
    {
        public string Code { get; }

        public SamPromptException(string code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    public sealed class UnsupportedMaskEncodingException : Exception
    {
        public UnsupportedMaskEncodingException(string message)
            : base(message)
        {
        }
    }

    public sealed class InvalidMaskPayloadException : Exception
    {
        public InvalidMaskPayloadException(string message)
            : base(message)
        {
            Code = "invalid_mask_payload";
        }

        public InvalidMaskPayloadException(string message, Exception inner)
            : base(message, inner)
        {
            Code = "invalid_mask_payload";
        }

        public string Code { get; }
    }

    public sealed class SparkSchemaException : Exception
    {
        public string Path { get; }

        public SparkSchemaException(string path, string message)
            : base(message)
        {
            Path = path;
        }
    }

    public sealed class SparkToolLimitException : Exception
    {
        public SparkToolLimitException(string message)
            : base(message)
        {
        }
    }
}
