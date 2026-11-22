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
            Current.BrokerCounter = Party.TransferOffer(memberId);
            return true;
        }

        public MarketReply OfferSale(string memberId, int asking)
        {
            if (!BrokerOpen) return MarketReply.Closed;
            if (asking < 1 || memberId != Current.BrokerMember || !Party.CanSellMember(memberId)) return MarketReply.Unavailable;
            int opening = Party.TransferOffer(memberId);
            int ceiling = opening + Math.Max(2, opening / 5);
            int room = ceiling - opening;
            int acceptable = Math.Max(Current.BrokerCounter, opening + room * (Current.BrokerRefusals + 1) / BrokerPatience);
            if (asking <= acceptable)
            {
                if (!Party.SellMember(memberId, asking)) return MarketReply.Unavailable;
                Current.BrokerMember = null;
                Current.BrokerCounter = 0;
                return MarketReply.Sold;
            }
            Current.BrokerRefusals++;
            if (!BrokerOpen) return MarketReply.Closed;
            Current.BrokerCounter = Math.Min(ceiling, opening + room * Current.BrokerRefusals / BrokerPatience);
            return MarketReply.Counteroffer;
        }
    }
}
