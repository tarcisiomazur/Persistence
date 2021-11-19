using System.Collections.Generic;
using System.Data.Common;

namespace Persistence
{
    public interface ISQL
    {
        public enum SqlTriggerType
        {
            BEFORE_INSERT,
            AFTER_INSERT,
            BEFORE_UPDATE,
            AFTER_UPDATE,
            BEFORE_DELETE,
            AFTER_DELETE
        }
        public string DefaultSchema { get; }
        protected internal bool ExistTable(Table table);
        protected internal DbDataReader LoadTable(Table table);
        protected internal bool ValidateField(Table table, Field field);
        protected internal bool ValidatePrimaryKeys(Table table, List<PrimaryKey> primaryKeys);
        protected internal bool ValidadeForeignKeys(Table table, Relationship relationship);
        protected internal string ConvertValueToString(object value);
        protected internal long InsertOrUpdate(Table table, Dictionary<string, object> fields);
        protected internal long Update(Table table, Dictionary<string, object> fields,
            Dictionary<PropColumn, object> keys);
        protected internal DbDataReader Select(Table table, Dictionary<string, object> keys, uint offset, uint length);
        protected internal bool Delete(Table table, Dictionary<string, object> keys);
        protected internal uint SelectCount(Table table, Dictionary<string, object> keys);
        protected internal uint SelectCountWhereQuery(Table table, string likeQuery);
        protected internal KeyType GetKeyType(string key);
        protected internal bool ExistTrigger(Table table, string triggerName);
        protected internal void CreateTrigger(Table table, string sqlTrigger, string triggerName,
            SqlTriggerType sqlTriggerType);
        protected internal DbDataReader SelectWhereQuery(Table table, string likeQuery, uint offset, uint length);
        protected internal DbDataReader ExecuteProcedure(string procedureName, Dictionary<string,object> parameters);
        protected internal DbDataReader SelectView(string name, string schema);
    }
}