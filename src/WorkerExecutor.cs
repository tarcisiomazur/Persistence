using System;
using System.Data;

namespace Persistence
{
    internal class WorkerExecutor
    {
        public IDbTransaction? DbTransaction { get; set; }
        public Action? OnCommit { get; set; }
        public Action? OnRollback { get; set; }

        public bool Commit()
        {
            try
            {
                DbTransaction?.Commit();
                DbTransaction?.Connection.Close();
                OnCommit?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Rollback()
        {
            try
            {
                DbTransaction.Rollback();
                OnRollback?.Invoke();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}