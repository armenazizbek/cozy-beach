using System.Collections.Generic;
using System.Linq;

// Port of src/core/Mail.ts: letters that arrive at the camp post a little while after a lost item is returned. One warm thank-you
// from each owner, keyed by the id of the Lost & Found note it answers.

public class Letter
{
    /// <summary>the Lost & Found note this answers</summary>
    public string id;
    public string from, greeting, text, sign;
}

public static class Mail
{
    public static readonly Letter[] LETTERS = new Letter[]
    {
        new Letter
        {
            id = "q_compass",
            from = "Marta",
            greeting = "Dear friend,",
            text = "This morning I held the compass up to the window and the needle swung round and settled, just the way Grandpa always swore it would. He used to say it points home. Today it did, in every sense. I cannot tell you what it means to have it back. Come by the shoreline any evening and I will pour you the best tea of your life.",
            sign = "With all my thanks, Marta",
        },
        new Letter
        {
            id = "q_key",
            from = "Old Pim",
            greeting = "Dear treasure hunter,",
            text = "I have been walking the dunes for forty years and I never once dropped a thing, until the captain's key. I told myself it was gone for good. Then your note came back to me through the board and I sat down right there on the sand and laughed. It is heavy and ornate and it opens nothing I can find, but it is mine again, and so I owe you a drink.",
            sign = "Gratefully yours, Old Pim",
        },
        new Letter
        {
            id = "q_ruby",
            from = "Juno",
            greeting = "Dear kind digger,",
            text = "The ruby is back on my finger and I have checked it about twenty times since breakfast. I was clamming, I was laughing, and then it was just gone. I did not think I would feel it again, that small weight on my hand. You gave me back a very good day. I will think of you every time it catches the sun.",
            sign = "Warmly, Juno",
        },
        new Letter
        {
            id = "q_quarters",
            from = "The Collector",
            greeting = "My dear friend,",
            text = "Two shiny silver quarters, as promised, and not a scratch on them! They now sit in the jar on my shelf where they clink most beautifully whenever the door shuts. I am told I have an odd hobby. I say it is the quietest, happiest hobby there is. Thank you for taking it seriously.",
            sign = "Sincerely, The Collector",
        },
        new Letter
        {
            id = "q_watch",
            from = "Grandma Ines",
            greeting = "Dear child,",
            text = "It does not tick any more, but when I hold my father's watch to my ear I can still hear him. That is enough. I had made my peace with losing it, and then you found it. I am an old woman and I cried like a girl. Please come for lemon cake. I insist, and I am very hard to refuse.",
            sign = "With love, Grandma Ines",
        },
        new Letter
        {
            id = "q_pearls",
            from = "Celeste",
            greeting = "Dearest hunter of lost things,",
            text = "I counted them twice: every pearl is still on the string. Every one was a birthday present, and I know exactly which birthday goes with which. It feels like having a whole row of my years returned to me. Thank you for digging so patiently and for caring about something so small and so huge.",
            sign = "Yours with a happy heart, Celeste",
        },
        new Letter
        {
            id = "q_glasses",
            from = "Rico",
            greeting = "Hey legend,",
            text = "The lucky sunglasses are back on my face and the world is cool again. Okay, I could see fine without them. But I looked worse, and now I look incredible, and that matters to me more than I will admit in person. You are officially invited to anything I ever host. It will be small and there will be snacks.",
            sign = "Stay shiny, Rico",
        },
        new Letter
        {
            id = "q_harmonica",
            from = "Busker Bo",
            greeting = "Dear friend of the music,",
            text = "I played the harmonica at the pier last night and it still has that one wheezy, lovely note. A little crowd gathered, which has never happened to me before. I think the note was waiting for you to find it. If you hear a tune drifting down the beach one evening, it is for you.",
            sign = "Keep listening, Bo",
        },
        new Letter
        {
            id = "q_wedding",
            from = "Dana",
            greeting = "Dear angel with a brush,",
            text = "I told my husband straight away. He went quiet, then he cried, and then we both laughed until we had to sit down. The little diamond is back where it belongs and we have decided to renew our promises on the beach, by the water, exactly where it was lost. You are our guest of honour. Please say yes.",
            sign = "All our love, Dana",
        },
        new Letter
        {
            id = "q_boat",
            from = "Little Mo",
            greeting = "Dear Mister or Missus Digger,",
            text = "My boat came home!!! It still has the red sail and the brave captain did not even get wet. I have given the captain a medal made of a bottle cap because he did a very big adventure. Thank you for finding him. When I am big I will dig things too. Maybe a dinosaur.",
            sign = "Your friend, Mo (age 7)",
        },
        new Letter
        {
            id = "q_pennies",
            from = "The Jar Lady",
            greeting = "Dear generous soul,",
            text = "Three old copper pennies, worn smooth as pebbles, and the jar is finally full to the top. I put it on the windowsill and the morning light turns it into a jar of little suns. I wanted you to know that you made one ordinary woman very, very pleased. The kettle is always on.",
            sign = "Fondly, the Jar Lady",
        },
        new Letter
        {
            id = "q_doubloon",
            from = "Captain Brine",
            greeting = "Ahoy, ye lucky sea-dog!",
            text = "A real pirate doubloon, and it was lying under the very sand I have been staring at for years. I be not a man to say this lightly: ye have a nose for treasure. The coin sits on the mantel now, gleaming, and every time I look at it I hear the waves. Fair winds and a full moon to ye.",
            sign = "Yer friend, Captain Brine",
        },
        new Letter
        {
            id = "q_denarius",
            from = "Professor Plum",
            greeting = "My dear colleague,",
            text = "I do not use the word lightly, but this is a remarkable day. An emperor's face, worn soft by two thousand years, now rests on my desk. I have written four pages about it already and I am only on the second sentence. You have made my whole year, as I said you would. Come and see it any time. I shall make tea and pretend to be calm.",
            sign = "With deepest appreciation, Professor Plum",
        },
    };

    public static readonly Dictionary<string, Letter> LETTER_BY_ID = LETTERS.ToDictionary(l => l.id);
}
