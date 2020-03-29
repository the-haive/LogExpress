using System.Collections.Generic;

namespace Flexy
{
    public class Lines : LinkedList<Line>
    {
        public void Add(long filePos, long num, string content)
        {
            var lineData = new Line()
            {
                Pos = filePos,
                Num = num,
                Content = content
            };

            var newNode = new LinkedListNode<Line>(lineData);

            if (First == null)
            {
                this.AddFirst(newNode);
            }
            else
            {

                this.AddAfter(Last, newNode);
            }
        }
    }

}