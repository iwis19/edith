using System;
using System.Collections.Generic;
using UnityEngine;

namespace Edith.Scrapbook
{
    /// Draws a deck with the few cards that can be seen at once. A card is bound to another memory as the deck
    /// scrolls past it, so the number of cards never grows with the number of memories.
    public sealed class DeckView
    {
        const int Cards = 2 * DeckLayout.Reach + 1;

        readonly MemoryCard[] cards = new MemoryCard[Cards];
        readonly Memory[] bound = new Memory[Cards];
        readonly int[] index = new int[Cards];
        int middle;

        /// `parent` is the deck's frame and must have unit world scale.
        public DeckView(Transform parent, Font font, Material material)
        {
            for (var i = 0; i < Cards; i++) cards[i] = MemoryCard.Build(parent, font, material);
        }

        /// Draws `memories` scrolled to `position`. `photoOf` gives a memory's photo by id, or null while it has none.
        /// `fade` scales every card's opacity. `form` below 1 draws a deck folded toward its middle, its cards fainter
        /// the more folded, except `steady`, which keeps its opacity. `skip` is left undrawn.
        public void Show(IReadOnlyList<Memory> memories, float position, Func<int, Texture> photoOf, float fade = 1f,
            float form = 1f, Memory steady = null, Memory skip = null)
        {
            middle = Mathf.RoundToInt(position);
            for (var at = middle - DeckLayout.Reach; at <= middle + DeckLayout.Reach; at++)
            {
                // Consecutive indices land on different cards, and an index keeps its card while it is in reach.
                var slot = (at % Cards + Cards) % Cards;
                var card = cards[slot];
                if (at < 0 || at >= memories.Count || memories[at] == skip)
                {
                    bound[slot] = null;
                    card.Hide();
                    continue;
                }
                if (bound[slot] != memories[at])
                {
                    bound[slot] = memories[at];
                    card.Show(memories[at]);
                }
                index[slot] = at;
                card.SetPhoto(photoOf(memories[at].Id));
                card.Place(DeckLayout.Pose((at - position) * form), memories[at] == steady ? fade : fade * form);
            }
        }

        public void Hide()
        {
            for (var slot = 0; slot < Cards; slot++)
            {
                bound[slot] = null;
                cards[slot].Hide();
            }
        }

        /// Draws one card in the middle that says `title` and `body`, in place of the deck.
        public void ShowNotice(string title, string body, float fade = 1f)
        {
            Hide();
            cards[0].Show(title, body, null, null, MemoryCard.Untiered);
            cards[0].SetPhoto(null);
            cards[0].Place(DeckLayout.Pose(0f), fade);
        }

        /// The index of the memory whose photo the line from `eye` through `through` crosses, or -1. Where cards
        /// overlap, the one nearer the middle wins, as it is the one drawn on top.
        public int Hit(Vector3 eye, Vector3 through)
        {
            for (var away = 0; away <= DeckLayout.Reach; away++)
                for (var side = -1; side <= 1; side += 2)
                {
                    var at = middle + side * away;
                    var slot = (at % Cards + Cards) % Cards;
                    if (bound[slot] != null && index[slot] == at && cards[slot].Shown && cards[slot].InLine(eye, through)) return at;
                }
            return -1;
        }
    }
}
