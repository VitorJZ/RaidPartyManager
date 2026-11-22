using System;

namespace RaidPartyManager.Run
{
    public enum MarketReply { Unavailable, Counteroffer, Sold, Closed }

    public sealed partial class AdventureRun
    {
        public const int BrokerPatience = 3;
        public bool BrokerOpen => CanUseTown(TownService.Recruit) && Current.BrokerRefusals < BrokerPatience;

        public bool DiscussSale(string memberId)
        {
            if (!BrokerOpen || !Party.CanSellMember(memberId)) return false;
            if (Current.BrokerMember == memberId && Current.BrokerCounter > 0) return true;
            Current.BrokerMember = memberId;
            Current.BrokerLastAsk = 0;
            Current.BrokerCounter = Party.TransferOffer(memberId);
            return true;
        }

        public MarketReply OfferSale(string memberId, int asking)
        {
            if (!BrokerOpen) return MarketReply.Closed;
            if (asking < 1 || memberId != Current.BrokerMember || !Party.CanSellMember(memberId)) return MarketReply.Unavailable;
            int opening = Party.TransferOffer(memberId);
            int counter = Current.BrokerCounter;
            int ceiling = Math.Max(counter, Party.BrokerBudget(memberId, opening));
            long room = ceiling - counter;
            bool concession = Current.BrokerLastAsk > asking;
            long acceptable = counter + room * (concession ? 60 : 25) / 100;
            if (asking <= acceptable)
            {
                if (!Party.SellMember(memberId, asking)) return MarketReply.Unavailable;
                Current.BrokerMember = null;
                Current.BrokerLastAsk = 0;
                Current.BrokerCounter = 0;
                return MarketReply.Sold;
            }
            Current.BrokerLastAsk = asking;
            Current.BrokerRefusals++;
            if (!BrokerOpen) return MarketReply.Closed;
            // A near-budget ask can draw a stronger counter. An extravagant anchor earns a smaller step.
            bool ambitious = asking > ceiling + room / 2;
            int share = ambitious ? 25 : concession ? 70 : 52;
            long step = Math.Max(3, (room * share + 50) / 100);
            Current.BrokerCounter = (int)Math.Min(asking - 1L, counter + Math.Min(room, step));
            return MarketReply.Counteroffer;
        }
    }
}
