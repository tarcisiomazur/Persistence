using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace Persistence
{
    public class SelectParameters
    {
        public SelectParameters(Table table)
        {
            Table = table;
        }

        public Table Table { get; set; }
        public Dictionary<string, object>? Keys{ get; set; }
        public uint? Offset{ get; set; }
        public uint? Length{ get; set; }
        public string? Where { get; set; }
        public string? OrderBy { get; set; }

    }
    public interface IPReader
    {
        public DbDataReader DataReader { get; set; }
        public void Close();
    }
    
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
        bool ExistTable(Table table);
        IPReader LoadTable(Table table);
        bool ValidateField(Table table, Field field);
        bool ValidatePrimaryKeys(Table table, List<PrimaryKey> primaryKeys);
        bool ValidadeForeignKeys(Table table, Relationship relationship);
        string ConvertValueToString(object value);
        long Insert(Table table, Dictionary<string, object> fields, ref IDbTransaction transaction);

        long Update(Table table, Dictionary<string, object> fields,
            Dictionary<PropColumn, object> keys, ref IDbTransaction transaction);
        IPReader Select(SelectParameters param);
        bool Delete(Table table, Dictionary<string, object> keys, ref IDbTransaction dbTransaction);
        uint SelectCount(Table table, Dictionary<string, object> keys);
        uint SelectCountWhereQuery(Table table, string likeQuery);
        KeyType GetKeyType(string key);
        bool ExistTrigger(Table table, string triggerName);
        void CreateTrigger(Table table, string sqlTrigger, string triggerName,
            SqlTriggerType sqlTriggerType);
        IPReader ExecuteProcedure(string procedureName, Dictionary<string,object> parameters);
        IPReader SelectView(string name, string schema);
    }
}