using System;
using System.Runtime.Serialization;

namespace Persistence
{
    public interface SQLException
    {
        const int ErrorCodeVersion = 40001;
        public int ErrorCode { get; set; }
    }

    [Serializable]
    public class PersistenceException : Exception
    {
        public PersistenceException()
        {

        }

        public PersistenceException(string message) : base(message)
        {

        }

        public PersistenceException(string message, Exception inner) : base(message, inner)
        {

        }

        protected PersistenceException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}