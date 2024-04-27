using Arcam.Data.DataBase;
using Arcam.Data.DataBase.DBTypes;

namespace ArcamFullPick
{
    public class StrategyMask
    {
        private List<IndicParamItem> indicators = [];
        private List<string> keysSorted = [];
        private Strategy baseStrat;
        private int baseParamCount;
        public StrategyMask(Strategy strat)
        {
            baseStrat = strat;
            for(var i =0; i < strat.StrategyIndicators.Count; i++)
            {
                var keys = strat.StrategyIndicators[i].InputFields.Keys.ToList();
                keys.Sort();
                keysSorted.AddRange(keys);
                for(var j = 0; j < keys.Count; j++)
                {
                    var item = new IndicParamItem
                    {
                        indicIdx = i,
                        count = strat.StrategyIndicators[i].InputFields.Count,
                        paramName = keys[j]
                    };
                    baseParamCount++;
                    indicators.Add(item);
                }
            }
        }
        static object locker = new object();
        public Strategy GetStrategy(List<int> paramValues, bool silent = false)
        {
            using var db = new ApplicationContext();

            var strat = new Strategy
            {
                Name = null,
                PairId = baseStrat.PairId,
                TimingId = baseStrat.TimingId,
                IsPublic = false,
                AuthorId = baseStrat.AuthorId,
                ModUserId = baseStrat.ModUserId,
                Leverage = baseStrat.Leverage,
                IsLong = baseStrat.IsLong,
                IsShort = baseStrat.IsShort
            };
            if (!silent)
                if (strat.Id <= 0)
                    db.Strategy.Add(strat);
                else
                    db.Strategy.Update(strat);
            else
                strat.Id = baseStrat.Id;
            db.SaveChanges();

            var j = 0;
            var idx = 0;
            for (var i = 0; i < baseStrat.StrategyIndicators.Count; i++)
            {
                var strategy = new StrategyIndicator
                {
                    IndicatorId = baseStrat.StrategyIndicators[i].IndicatorId,
                    IsExit = baseStrat.StrategyIndicators[i].IsExit,
                    StrategyId = strat.Id
                };
                if (strategy.Id > 0)
                    db.StrategyIndicator.Update(strategy);
                else
                    db.StrategyIndicator.Add(strategy);
                db.SaveChanges();
                strategy.InputFields = [];

                var fields = baseStrat.StrategyIndicators[i].InputFields;
                var cnt = fields.Count;
                for (var k = 0; k < cnt; k++, j++)
                {
                    var key = keysSorted[j];
                    var field = new InputField
                    {
                        IndicatorFieldId = fields[key].IndicatorFieldId,
                        StrategyIndicatorId = strategy.Id,
                        IntValue = paramValues[j],
                        FloatValue = null
                    };
                    db.InputField.Add(field);

                    db.SaveChanges();
                    strategy.InputFields[key] = field;
                }

                if (!silent)
                    db.StrategyIndicator.Update(strategy);
                db.SaveChanges();
                strategy.Indicator = baseStrat.StrategyIndicators[i].Indicator;
            }

            if (!silent)
            {
                var copies = db.TestStrategy.Where(x => x.StrategyHash == strat.GetHashCode()).Count();
                if (copies == 0)
                    db.SaveChanges();
            }

            strat.Pair = baseStrat.Pair;
            strat.Timing = baseStrat.Timing;

            return strat;
        }

        public List<int> GetParams(Strategy strat)
        {
            var res = new List<int>();
            var k = 0;
            for (var i = 0; i < strat.StrategyIndicators.Count; i++)
            {
                for (var j = 0; j < strat.StrategyIndicators[i].InputFields.Count; j++)
                {
                    res.Add(strat.StrategyIndicators[i].InputFields[keysSorted[k]].IntValue ?? 0);
                    k++;
                }
            }

            return res;
        }

        private class IndicParamItem()
        {
            public int indicIdx = 0;
            public string paramName = "";
            public int count = 0;
        }
    }
}
