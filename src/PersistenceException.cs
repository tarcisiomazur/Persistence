using System;
using System.Runtime.Serialization;

namespace Persistence
{
    public abstract class SQLException: Exception
    {
        public const int ErrorCodeVersion = 40001;
        public abstract int ErrorCode { get; set; }

        public SQLException() : base()
        {
        }

        public SQLException(string message) : base(message)
        {

        }

        public SQLException(string message, Exception inner) : base(message, inner)
        {

        }
    }

    [Serializable]
    public class PersistenceException : Exception
    {
        public int ErrorCode { get; set; }
        
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