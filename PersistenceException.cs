using System;
using System.Runtime.Serialization;

namespace Persistence
{
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