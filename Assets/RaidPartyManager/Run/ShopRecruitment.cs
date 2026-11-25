using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Domain;

namespace RaidPartyManager.Run
{
    public sealed class ShopRecruitment
    {
        public const int SlotCount = 8;
        public const int StartingPrice = 15;
        private readonly PartyRun party;
        private readonly List<CharacterDefinition> candidates = new();

        public IReadOnlyList<CharacterDefinition> Slots { get; }
        public IReadOnlyList<CharacterDefinition> Candidates { get; }
        public bool Signed { get; private set; }
        public int NextPrice { get; private set; }
        public bool CanDraw => !Signed && Slots.Any(IsAvailable);

        public ShopRecruitment(PartyRun party, IEnumerable<CharacterDefinition> characters, Random random, int price = StartingPrice)
            : this(party, CreateSlots(party, characters, random), price)
        {
        }

        private ShopRecruitment(PartyRun party, CharacterDefinition[] slots, int price)
        {
            this.party = party;
            Slots = Array.AsReadOnly(slots);
            Candidates = candidates.AsReadOnly();
            NextPrice = price;
        }

        private static CharacterDefinition[] CreateSlots(PartyRun party, IEnumerable<CharacterDefinition> characters, Random random)
        {
            var owned = party.Members.Select(member => member.Character.Id).ToHashSet();
            var eligible = characters.Where(character => character.RosterNumber > 0 && character.Id != AdventureRun.TideSingerId && !AdventureRun.IsGardenerForm(character.Id) && !owned.Contains(character.Id))
                .GroupBy(character => character.Id).Select(group => group.First()).ToArray();
            var featured = eligible.Where(character => character.Stage == 2).OrderBy(_ => random.Next()).FirstOrDefault();
            var slots = eligible.Where(character => character.Stage == 1).OrderBy(_ => random.Next())
                .Take(SlotCount - 1).ToList();
            if (featured != null) slots.Insert(random.Next(slots.Count + 1), featured);
            var wheel = slots.ToArray();
            Array.Resize(ref wheel, SlotCount);
            return wheel;
        }

        public bool IsAvailable(CharacterDefinition slot) => slot != null && !candidates.Contains(slot) &&
            party.Members.All(member => member.Character.Id != slot.Id);

        public bool TryDraw(Random random, Func<int, bool> trySpend)
        {
            if (Signed) return false;
            var remaining = Slots.Where(IsAvailable).ToArray();
            if (remaining.Length == 0 || !trySpend(NextPrice)) return false;
            candidates.Add(remaining[random.Next(remaining.Length)]);
            NextPrice = (NextPrice + 5) % 100;
            return true;
        }

        public bool CanSign(string characterId) => !Signed && candidates.Any(character => character.Id == characterId) &&
            party.Members.All(member => member.Character.Id != characterId);

        public bool TrySign(string characterId, string replacedMemberId = null)
        {
            if (!CanSign(characterId)) return false;
            var candidate = candidates.First(character => character.Id == characterId);
            if (!(replacedMemberId == null ? party.TryRecruit(candidate) : party.TryReplaceMember(replacedMemberId, candidate)))
                return false;
            Signed = true;
            return true;
        }

        internal static ShopRecruitment Restore(PartyRun party, IReadOnlyDictionary<string, CharacterDefinition> byId,
            string[] slotIds, string[] drawnIds, bool signed, int price)
        {
            if (slotIds.Length != SlotCount || signed && drawnIds.Length == 0 ||
                slotIds.Where(id => !string.IsNullOrEmpty(id)).GroupBy(id => id).Any(group => group.Count() > 1) ||
                drawnIds.Distinct().Count() != drawnIds.Length || drawnIds.Any(id => string.IsNullOrEmpty(id) || !slotIds.Contains(id)))
                throw new ArgumentException("Invalid shop candidates.");
            var slots = slotIds.Select(id => string.IsNullOrEmpty(id) ? null : byId[id]).ToArray();
            var recruitment = new ShopRecruitment(party, slots, price) { Signed = signed };
            recruitment.candidates.AddRange(drawnIds.Select(id => byId[id]));
            return recruitment;
        }
    }
}
