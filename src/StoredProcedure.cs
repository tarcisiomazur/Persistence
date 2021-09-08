using System;
using System.Collections.Generic;
using System.Linq;

namespace Persistence
{
    public class StoredProcedure<T>
    {
        private List<string> Parameters;
        public string ProcedureName { get; }
        public StoredProcedure(string procedureName, params string[] param)
        {
            ProcedureName = procedureName;
            Parameters = param.ToList();
        }
        
        public IEnumerable<T> Execute(params object[] param)
        {
            var dict = Parameters.Zip(param).ToDictionary(arg => arg.First, arg=>arg.Second);
            
            var reader = Persistence.Sql.ExecuteProcedure(ProcedureName, dict);
            var typeT = typeof(T);
            var r = new List<T>();
            if (typeT.IsSubclassOf(typeof(DAO)))
            {
                var type = typeof(PList<>).MakeGenericType(typeT);
                var list = (IPList) Activator.CreateInstance(type);
                list.BuildList(reader);
                return (IEnumerable<T>) list;
            }

            var fields = reader.GetFields();
            while (reader.Read())
            {
                var obj = (T) Activator.CreateInstance(typeT);
                foreach (var pi in typeT.GetProperties())
                {
                    var index = fields.FindIndex(s => s.Equals(pi.Name, StringComparison.CurrentCultureIgnoreCase));
                    if (index>=0)
                    {
                        pi.SetValue(obj,reader[index]);
                    }
                }
                r.Add(obj);
            }
            return r;
        }
    }
}