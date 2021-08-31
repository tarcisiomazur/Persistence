using System.Collections.Generic;
using System.Linq;

namespace Persistence
{
    public class Relationship : PropColumn
    {
        public readonly Dictionary<string,Field> Links;
        public RelationshipType Type { get; set; }
        public string FkName => GetFkName();
        public Cascade Cascade { get; }
        public Fetch Fetch { get; }
        public FkOptions OnDelete { get; }
        public FkOptions OnUpdate { get; }
        public string ReferenceName { get; internal set; }
        public Table TableReferenced { get; internal set; }

        public Relationship(ManyToOneAttribute m2o)
        {
            ReferenceName = m2o.ReferencedName;
            Nullable = m2o.Nullable;
            Cascade = m2o.Cascade;
            Fetch = m2o.Fetch;
            Links = new Dictionary<string, Field>();
        }
        public Relationship()
        {
            Nullable = Nullable.NotNull;
            Cascade = Cascade.NULL;
            Fetch = Fetch.Eager;
            OnUpdate = FkOptions.CASCADE;
            Links = new Dictionary<string, Field>();
        }

        private string GetFkName()
        {
            var name = $"fk_{Table.SqlName}_{ReferenceName}";
            if (Links.Count == 1)
                name += $"_{Links.Values.First().SqlName}";
            return name;

        }

        public void AddKey(Field field)
        {
            Links.Add($"{TableReferenced.SqlName}_{field.SqlName}", field);
        }
    }
}