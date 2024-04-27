using NLog;

namespace ArcamFullPick
{
    public class GA
    {
        public static int ChildrensCount = 128;
        Logger logger = LogManager.GetCurrentClassLogger();
        private int paramCount = 0;
        public List<List<int>> GenerateInitGeneration(List<int> odin)
        {
            var res = new List<List<int>>();
            res.Add(odin);
            paramCount = odin.Count;
            var rand = new Random((int)DateTime.Now.Ticks);
            for (var i = 1; i < ChildrensCount; i++)
            {
                var thor = new List<int>();
                for (var j = 0; j < paramCount; j++)
                {
                    thor.Add(odin[j]);
                }
                for (var j = 0; j < 10; j++)
                {
                    var idx = rand.Next(0, odin.Count);
                    thor[idx] = rand.Next(1, 200);
                }
                res.Add(thor);
            }
            return res;
        }
        
        public List<List<int>> GetNextGeneration(List<GAPriority> priorities, bool forceMutation)
        {
            double fullSize = 0;

            priorities.Sort();
            for (var i = 0; i < priorities.Count; i++) 
            {
                fullSize += priorities[i].priority;
            }

            var parents = new List<List<int>>();
            for (var i = 0; i < 7; i++)
                parents.Add(priorities[i].param);

            if (forceMutation || priorities[priorities.Count - 1].priority / priorities[6].priority > 0.8 || priorities[0].priority < fullSize / (priorities.Count - 1))
            {
                parents = MutateParents(parents);
            }

            var res = new List<List<int>>();
            res.AddRange(parents);

            var rand = new Random((int)DateTime.Now.Ticks);
            var ranges = new List<int>();
            var rangeMax = paramCount - 2;
            for (var i = 0; i < 3; i++)
            {
                var newIdx = rand.Next(1, rangeMax);
                while (ranges.Contains(newIdx) || ranges.Contains(newIdx - 1) || ranges.Contains(newIdx + 1))
                {
                    newIdx = rand.Next(1, rangeMax);
                }
                ranges.Add(newIdx);
            }
            ranges.Sort();
            var parentItem = 0;
            while (res.Count < ChildrensCount)
            {
                if (parents[parentItem].Count != paramCount || parents[parentItem + 1].Count != paramCount)
                    throw new Exception("WTF " + parents[parentItem].Count + " " + parents[parentItem + 1].Count);
                var childA = new List<int>();
                var childB = new List<int>();
                var switcher = false;
                for(var i = 0; i < ranges[0]; i++) 
                {
                    childA.Add(parents[parentItem][i]);
                    childB.Add(parents[parentItem + 1][i]);
                }
                for (var i = 0; i < ranges.Count - 1; i++)
                {
                    for (var j = ranges[i]; j < ranges[i + 1]; j++)
                    {
                        if (switcher)
                        {
                            childA.Add(parents[parentItem][j]);
                            childB.Add(parents[parentItem + 1][j]);
                        }
                        else
                        {
                            childA.Add(parents[parentItem + 1][j]);
                            childB.Add(parents[parentItem][j]);
                        }
                        switcher = !switcher;
                    }
                }
                for (var i = ranges[ranges.Count - 1]; i < paramCount; i++)
                {
                    if (switcher)
                    {
                        childA.Add(parents[parentItem][i]);
                        childB.Add(parents[parentItem + 1][i]);
                    }
                    else
                    {
                        childA.Add(parents[parentItem + 1][i]);
                        childB.Add(parents[parentItem][i]);
                    }
                }
                if (childA.Count != paramCount || childB.Count != paramCount)
                {
                    logger.Error(ranges[0] + " " + ranges[1] + " " + ranges[2]);
                    throw new Exception("WTF2 " + childA.Count + " " + childB.Count);
                }

                res.Add(childA);
                res.Add(childB);
                parentItem++;
                if (parentItem >= parents.Count - 2)
                    parentItem = 0;
            }

            return res;
        }

        private List<List<int>> MutateParents(List<List<int>> parents)
        {
            logger.Warn("Mutating");
            var cnt = paramCount - 1;
            var rand = new Random((int)DateTime.Now.Ticks);
            
            for (var i = 0; i < cnt; i++)
            {
                parents[rand.Next(0, parents.Count - 1)][rand.Next(0, cnt)] = rand.Next(1, 200);
            }

            return parents;
        }

        public class GAPriority : IComparable<GAPriority>
        {
            public double priority;
            public List<int> param;

            public int CompareTo(GAPriority? obj)
            { 
                if (obj == null)
                    return 1;
                return -priority.CompareTo(obj.priority);
            }
        }
    }
}
