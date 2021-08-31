using System.Reflection;

namespace Persistence
{
    public class PropColumn
    {
        public string SqlName { get; internal set; }
        public Table Table { get; internal set; }
        public PropertyInfo Prop { get; internal set; }
        protected internal bool Persisted { get; internal set; }
        public Nullable Nullable { get; internal set; }

        public PropColumn(PropertyInfo prop)
        {
            Prop = prop;
        }

        public PropColumn()
        {
        }
    }
}