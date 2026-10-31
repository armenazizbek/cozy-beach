/** One entry of the captain's journal, which lies on the stone ring in the room under the hatch. */
export interface StoryPage {
  heading: string;
  text: string;
  sign: string;
}

export const CAPTAIN_PAGES: StoryPage[] = [
  {
    heading: 'The first day out',
    text: 'We cleared the harbour at dawn with the whole town’s savings in the hold: forty bars of gold, melted down from every spoon and locket the village could spare, and the old church bell besides. Nobody trusted the bank, and everybody trusted me. I have never carried anything so heavy that weighed so little on my mind. The sea was flat as a plate and the Marigold sang all morning.',
    sign: 'Captain M. Holt',
  },
  {
    heading: 'The sixth day',
    text: 'The glass has dropped like a stone. The crew pretend not to look at it. The gulls have all gone inland, which no sailor likes to see, and the water has gone the colour of an old coin. I have had the bars lashed down twice over. If the weather means to have the gold, it will have to take the ship with it.',
    sign: 'M. Holt',
  },
  {
    heading: 'The wreck',
    text: 'She went onto the reef in the dark. I will not write down the sound. The crew got into the boats and reached the island, and I can see their fire from here, small and steady. I was thrown the other way, and woke on a long beach of pale sand under a sky that looked ashamed of itself. The Marigold is lying on the bottom in the deep, and the gold with her.',
    sign: 'M. Holt',
  },
  {
    heading: 'Under the dunes',
    text: 'Looking for fresh water I fell through the sand into a room. Real stone, laid by someone much older than me, with a stair going up at one end and a great ring cut into the floor. It is dry. It is quiet. It is the only place on this whole coast where the wind cannot reach me. I have decided to live in it until the boats come back.',
    sign: 'M. Holt',
  },
  {
    heading: 'A door, and a lamp',
    text: 'I have made the stair safe and hung a ladder up to the roof of the shaft, and built a hatch to close the top, because the wind blows sand into everything. I have lit torches along the walls. In the firelight the stone looks almost warm, and I find I talk to it in the evenings. It does not answer, but it listens better than the crew did.',
    sign: 'M. Holt',
  },
  {
    heading: 'The bottle',
    text: 'There is no ship coming for a while, so I have done what a sailor does. I drew a map of this beach, marked the hatch, wrote a line asking whoever finds it to be kind, and sealed it in a green bottle. I carried it into the surf and let the current take it down to where the water is deepest. Someone patient will find it. I left another map on the altar of the island, for the crew, in case I do not make it back.',
    sign: 'M. Holt',
  },
  {
    heading: 'The great door',
    text: 'At the far end of the room stands a door of black wood and iron, older than the stone around it. There is no keyhole for a key: only a round, flat lock the size of a large medallion, with fine rings cut around it like those of a navigator\u2019s astrolabe. Someone cut a narrow shaft through the roof, and at noon the sun comes down it and lands on that lock, as if whoever built this place had planned it so. I own neither of those things, and I find I do not want to be the one who opens it.',
    sign: 'M. Holt',
  },
  {
    heading: 'The last entry',
    text: 'The boats are back. The crew came across the water at low tide, thin and sunburnt and cheerful, and we have decided to sail for home at first light. I never found the gold. The sea has it now, and the sea has always known how to keep a secret. I will leave this journal where I found the room, on the ring, and the torches burning. To whoever reads this: look after the place. And mind the great door.',
    sign: 'Captain M. Holt',
  },
];
