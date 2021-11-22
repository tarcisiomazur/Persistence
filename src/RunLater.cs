using System;
using Priority_Queue;

namespace Persistence
{
    public class RunLater
    {
        private readonly SimplePriorityQueue<Action, int> _functions = new SimplePriorityQueue<Action, int>();

        public void Later(short priority, Action action)
        {
            _functions.Enqueue(action, priority);
        }

        public void Later(Action action)
        {
            _functions.Enqueue(action, 0);
        }

        public void Run()
        {
            _functions.Do(action => action());
            _functions.Clear();
        }

        public void Clear()
        {
            _functions.Clear();
        }
    }
}