using System;
using Priority_Queue;

namespace Persistence
{
    internal class RunLater
    {
        private readonly SimplePriorityQueue<Action<WorkerExecutor>, int> _functions = new ();
        private WorkerExecutor _executor;
        public RunLater(WorkerExecutor executor)
        {
            _executor = executor;
        }

        public void Later(short priority, Action<WorkerExecutor> action)
        {
            _functions.Enqueue(action, priority);
        }

        public void Later(Action<WorkerExecutor> action)
        {
            _functions.Enqueue(action, 0);
        }

        public void Run()
        {
            _functions.Do(action => action(_executor));
            _functions.Clear();
        }

        public void Clear()
        {
            _functions.Clear();
        }
    }
}