using System.Collections.Generic;

/// <summary>
/// Little pictures of what the camp sells, for the Upgrades and Luxury pages, so the player can see what they are buying.
/// Each one is drawn on a 64 by 64 grid in the colours of the thing in the game.
/// </summary>
public static class ShopArt
{
    private const string OPEN = "<svg viewBox='0 0 64 64' width='60px' height='60px'>";

    // the detector of the hotbar, drawn twice as big; `shaft`, `box` and `ring` are its colours
    private static string detector(string shaft, string box, string ring, string screen, string extra = "") =>
        "<g transform='scale(2)'>" +
        $"<ellipse cx='9.5' cy='25.5' rx='7.5' ry='3.6' fill='#1f2124' stroke='{ring}' stroke-width='1.8'/>" +
        "<circle cx='9.5' cy='25.5' r='2.1' fill='#3a4048'/>" +
        $"<path d='M9.5 25L21 11.5L23.5 5.5L27.5 7' fill='none' stroke='{shaft}' stroke-width='2.4' stroke-linecap='round' stroke-linejoin='round'/>" +
        "<path d='M25.5 3.5C28.5 3.8 30 6.2 29.2 9' fill='none' stroke='#1f2124' stroke-width='2.6' stroke-linecap='round'/>" +
        "<path d='M21.8 10.5L23.2 6.8' stroke='#1f2124' stroke-width='4' stroke-linecap='round'/>" +
        $"<path d='M14.2 19.5L15.8 17.6' stroke='{ring}' stroke-width='3.2' stroke-linecap='round'/>" +
        $"<g transform='translate(18.8 13.8) rotate(-49)'><rect x='-3.4' y='-3.2' width='6.8' height='5.2' rx='1.2' fill='{box}' stroke='#1f2124' stroke-width='1'/>" +
        $"<rect x='-2' y='-2' width='4' height='2.4' rx='0.4' fill='{screen}'/></g>" + extra + "</g>";

    private static string trowel(string blade, string edge, string grip) =>
        "<g transform='scale(2)'>" +
        $"<path d='M3 29C2 20 7 13 14 11L21 18C19 25 12 30 3 29Z' fill='{blade}' stroke='{edge}' stroke-width='1.4' stroke-linejoin='round'/>" +
        $"<path d='M4.5 27.5L16 16' stroke='{edge}' stroke-width='1.4' stroke-linecap='round'/>" +
        $"<path d='M16 16L22 10' stroke='{edge}' stroke-width='2.6'/>" +
        $"<path d='M21 11L23 9' stroke='{blade}' stroke-width='5'/>" +
        $"<path d='M23.5 8.5L28.5 3.5' stroke='{grip}' stroke-width='5.4' stroke-linecap='round'/></g>";

    private static string bone(string fill, string edge) =>
        $"<g fill='{fill}' stroke='{edge}' stroke-width='2' stroke-linejoin='round'>" +
        "<path d='M17 37L37 17C35 13 37 8 41.5 8C45 8 46.5 11 46 13.5C49 13 52 15 52 18.5C52 23 47 25 43.5 23.5L23.5 43.5C25 47 23 52 18.5 52C15 52 13 49 13.5 46C11 46.5 8 45 8 41.5C8 37 13 35 17 37Z'/></g>" +
        $"<path d='M22 39L39 22' stroke='{edge}' stroke-width='1.4' opacity='0.35'/>";

    private static string cart(string paint, string seat, string roof) =>
        // the frame and the roof on its posts
        $"<path d='M14 14H50' stroke='{roof}' stroke-width='5' stroke-linecap='round'/>" +
        "<path d='M18 16L17 34M46 16L48 34' stroke='#3a3f45' stroke-width='2.4' stroke-linecap='round'/>" +
        // the body, the seat, the wheels
        $"<path d='M6 44C6 37 9 34 14 34H44L52 30C56 30 58 33 58 37V44C58 46 57 47 55 47H9C7 47 6 46 6 44Z' fill='{paint}' stroke='#5a4a2a' stroke-width='1.6' stroke-linejoin='round'/>" +
        $"<path d='M20 34V25C20 23 21 22 23 22H31C33 22 34 23 34 25V34Z' fill='{seat}' stroke='#6b5a3c' stroke-width='1.4'/>" +
        $"<rect x='18' y='31' width='20' height='4' rx='1.5' fill='{seat}' stroke='#6b5a3c' stroke-width='1.2'/>" +
        "<path d='M46 30L42 22' stroke='#2b2f35' stroke-width='2' stroke-linecap='round'/><ellipse cx='41.5' cy='21.5' rx='3' ry='1.4' fill='#2b2f35'/>" +
        "<circle cx='17' cy='48' r='6' fill='#22252a'/><circle cx='17' cy='48' r='2.6' fill='#c9ced4'/>" +
        "<circle cx='48' cy='48' r='6' fill='#22252a'/><circle cx='48' cy='48' r='2.6' fill='#c9ced4'/>";

    private static string boat(string hull, string stripe, string rail, string extra = "") =>
        "<path d='M4 50C14 47 22 53 32 50C42 47 50 53 60 50' fill='none' stroke='#5b9fd0' stroke-width='2.4' stroke-linecap='round'/>" +
        $"<path d='M5 33H59L53 46C52 47.5 50.5 48 49 48H15C13 48 12 47.5 11 46Z' fill='{hull}' stroke='#3c4650' stroke-width='1.6' stroke-linejoin='round'/>" +
        $"<path d='M7 37H57' stroke='{stripe}' stroke-width='3.6'/>" +
        $"<path d='M5 33H59' stroke='{rail}' stroke-width='2.4' stroke-linecap='round'/>" +
        // the little wheelhouse and its windscreen
        "<path d='M20 33V22C20 20.5 21 19.5 22.5 19.5H38L43 33Z' fill='#f2eee4' stroke='#3c4650' stroke-width='1.5' stroke-linejoin='round'/>" +
        "<path d='M24 22.5H36.5L39.5 29H24Z' fill='#6ea9cf'/>" +
        "<path d='M30 19.5V12' stroke='#3c4650' stroke-width='1.6'/><circle cx='30' cy='11' r='1.6' fill='#d94b3e'/>" + extra;

    private static readonly Dictionary<string, string> ART = new Dictionary<string, string>
    {
        // ---------------------------------------------------------------- the detectors
        ["detector_1"] = detector("#9a6a44", "#6d5141", "#a0703f", "#7d8a5a",
            "<circle cx='19' cy='14' r='0.9' fill='#b8743a'/><circle cx='12.5' cy='21.5' r='0.8' fill='#b8743a'/><circle cx='16.6' cy='16.8' r='0.6' fill='#7a4a28'/>"),
        ["detector_2"] = detector("#9aa3ad", "#3a4048", "#e6b422", "#39ff88",
            "<ellipse cx='9.5' cy='25.5' rx='5' ry='2.2' fill='none' stroke='#e6b422' stroke-width='0.9'/>"),
        ["detector_3"] = detector("#c9ced4", "#2a3340", "#38b6e8", "#ff7a3a",
            "<path d='M2 30.5C6 28.8 13 28.8 17 30.5' fill='none' stroke='#ff9a52' stroke-width='1.1' stroke-linecap='round' opacity='0.85'/>" +
            "<path d='M4.5 31.6C7 30.6 12 30.6 14.5 31.6' fill='none' stroke='#ffd25a' stroke-width='0.9' stroke-linecap='round' opacity='0.85'/>"),
        ["gold_detector"] = detector("#f0c24a", "#d8a830", "#f0c24a", "#39ff88",
            "<path d='M24 3L24.6 4.4L26 5L24.6 5.6L24 7L23.4 5.6L22 5L23.4 4.4Z' fill='#fff6c8'/><path d='M5 18L5.4 19L6.4 19.4L5.4 19.8L5 20.8L4.6 19.8L3.6 19.4L4.6 19Z' fill='#fff6c8'/>"),

        // ---------------------------------------------------------------- the digging tools
        ["digging_1"] = trowel("#c9cfd6", "#5f6771", "#e0732f"),
        ["digging_2"] =
            "<g transform='scale(2)'><path d='M3 18L19 11L25 21C21 30 8 31 3 25Z' fill='#dfe4ea' stroke='#5f6771' stroke-width='1.4' stroke-linejoin='round'/>" +
            "<g fill='#5f6771'><circle cx='9' cy='21' r='1.3'/><circle cx='14' cy='19' r='1.3'/><circle cx='12' cy='25' r='1.3'/><circle cx='18' cy='23' r='1.3'/><circle cx='7' cy='26' r='1.1'/></g>" +
            "<path d='M19 13L22 9' stroke='#9aa3ad' stroke-width='2.6'/><path d='M22 9L28 3.5' stroke='#1f2124' stroke-width='5.4' stroke-linecap='round'/></g>",
        ["digging_3"] =
            "<g transform='scale(2)'><path d='M5 17L13 14L19 20C18 28 10 31 5 29C3 25 3 20 5 17Z' fill='#c9cfd6' stroke='#5f6771' stroke-width='1.4' stroke-linejoin='round'/>" +
            "<path d='M12 19L25 6' stroke='#9aa3ad' stroke-width='2.6' stroke-linecap='round'/><path d='M22 9L26.5 4.5' stroke='#1f2124' stroke-width='4.4' stroke-linecap='round'/>" +
            "<path d='M23.5 3L29 8.5' stroke='#1f2124' stroke-width='3.4' stroke-linecap='round'/></g>",
        ["gold_trowel"] =
            trowel("#f0c24a", "#a8801e", "#4a2e1c") +
            "<path d='M14 36L15 38.4L17.4 39.4L15 40.4L14 42.8L13 40.4L10.6 39.4L13 38.4Z' fill='#fff6c8'/>",

        // ---------------------------------------------------------------- the cleaning tools
        ["cleaning_1"] =
            // an old toothbrush, its bristles splayed with use
            "<g transform='rotate(-35 32 32)'><rect x='4' y='29' width='44' height='7' rx='3.5' fill='#4fa3d9' stroke='#2b6e98' stroke-width='1.4'/>" +
            "<rect x='10' y='31' width='14' height='3' rx='1.5' fill='#bfe3f7' opacity='0.8'/>" +
            "<rect x='45' y='28' width='15' height='9' rx='3' fill='#4fa3d9' stroke='#2b6e98' stroke-width='1.4'/>" +
            "<g stroke='#f4f1e8' stroke-width='2.2' stroke-linecap='round'><path d='M47 28L45 19'/><path d='M50.5 28L50 18'/><path d='M54 28L55 18.5'/><path d='M57.5 28L60 20'/></g>" +
            "<g stroke='#cfc8b6' stroke-width='1.2' stroke-linecap='round'><path d='M48.7 28L47.6 20'/><path d='M52.2 28L52.4 19'/><path d='M55.8 28L57.6 19.5'/></g></g>",
        ["cleaning_2"] =
            // a wooden brush with a brass band and dark horsehair
            "<g transform='rotate(-35 32 32)'><path d='M4 28H34C36 28 37 29.5 37 32.5C37 35.5 36 37 34 37H4C2.5 37 2 35.5 2 32.5C2 29.5 2.5 28 4 28Z' fill='#a86b3c' stroke='#6b3f1f' stroke-width='1.4'/>" +
            "<path d='M7 31H30' stroke='#c98b55' stroke-width='1.4' stroke-linecap='round'/><circle cx='6.5' cy='32.5' r='1.6' fill='#6b3f1f'/>" +
            "<rect x='35' y='26' width='8' height='13' rx='1.5' fill='#d9ab45' stroke='#8f6a1f' stroke-width='1.4'/><path d='M37.5 27V38' stroke='#f3d58a' stroke-width='1.2'/>" +
            "<path d='M43 25H58C60 25 61 26 61 27.5V37.5C61 39 60 40 58 40H43Z' fill='#3b2d24' stroke='#22180f' stroke-width='1.4'/>" +
            "<g stroke='#5e4a3c' stroke-width='1' stroke-linecap='round'><path d='M46 27V38'/><path d='M50 27V38'/><path d='M54 27V38'/><path d='M58 27.5V37.5'/></g></g>",
        ["cleaning_3"] =
            // a steel bath of water with bubbles, and its dial
            "<path d='M8 24H56V50C56 53 54 55 51 55H13C10 55 8 53 8 50Z' fill='#c9ced4' stroke='#5f6771' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<path d='M12 28H52V40H12Z' fill='#6fb6dc'/><path d='M12 28H52' stroke='#bfe6f7' stroke-width='1.6'/>" +
            "<g fill='none' stroke='#e9f7ff' stroke-width='1.2'><circle cx='20' cy='34' r='2'/><circle cx='26' cy='31.5' r='1.3'/><circle cx='33' cy='35' r='2.4'/><circle cx='41' cy='32' r='1.6'/><circle cx='46' cy='36' r='1.2'/></g>" +
            "<g fill='none' stroke='#bfe6f7' stroke-width='1.4' stroke-linecap='round'><path d='M22 22C22 19 24 19 24 16'/><path d='M32 21C32 18 34 18 34 15'/><path d='M42 22C42 19 44 19 44 16'/></g>" +
            "<rect x='12' y='43' width='16' height='8' rx='1.5' fill='#2b3036'/><rect x='14' y='45' width='9' height='4' rx='0.6' fill='#39ff88'/>" +
            "<circle cx='44' cy='47' r='4.2' fill='#3a4048' stroke='#1f2124' stroke-width='1'/><path d='M44 47L46.4 44.6' stroke='#e6b422' stroke-width='1.6' stroke-linecap='round'/>" +
            "<rect x='6' y='22' width='52' height='4' rx='1.5' fill='#9aa3ad' stroke='#5f6771' stroke-width='1.2'/>",

        // ---------------------------------------------------------------- the thermos and gear
        ["gear_1"] =
            // a speckled blue enamel mug of coffee
            "<g fill='none' stroke='#d6ccbd' stroke-width='1.8' stroke-linecap='round'><path d='M24 18C24 14 27 14 27 10'/><path d='M32 17C32 13 35 13 35 9'/></g>" +
            "<path d='M44 30H48C52 30 54 33 54 37C54 41 52 44 48 44H44' fill='none' stroke='#3c6fa0' stroke-width='4'/>" +
            "<path d='M14 22H46V48C46 51 44 53 41 53H19C16 53 14 51 14 48Z' fill='#4a82b8' stroke='#2d567f' stroke-width='1.6'/>" +
            "<ellipse cx='30' cy='22' rx='16' ry='3.2' fill='#5a3a24' stroke='#e8e2d6' stroke-width='2'/>" +
            "<g fill='#e8e2d6'><circle cx='20' cy='32' r='1'/><circle cx='29' cy='38' r='0.9'/><circle cx='38' cy='30' r='1.1'/><circle cx='24' cy='45' r='0.9'/><circle cx='40' cy='44' r='1'/><circle cx='34' cy='49' r='0.8'/></g>" +
            "<path d='M14 50C14 52 16 53 19 53H41C44 53 46 52 46 50' fill='none' stroke='#e8e2d6' stroke-width='1.6'/>",
        ["gear_2"] =
            // a tall green thermos, its cup-lid off and steaming with chai
            "<rect x='13' y='12' width='22' height='46' rx='5' fill='#3f7f5e' stroke='#23513a' stroke-width='1.6'/>" +
            "<rect x='13' y='20' width='22' height='4' fill='#2b5c43'/><rect x='13' y='46' width='22' height='4' fill='#2b5c43'/>" +
            "<rect x='16' y='26' width='4' height='18' rx='2' fill='#7fb89a' opacity='0.6'/>" +
            "<rect x='16' y='7' width='16' height='7' rx='2' fill='#c9ced4' stroke='#5f6771' stroke-width='1.4'/>" +
            "<path d='M39 38H55L53 54C53 55.5 52 56 50.5 56H43.5C42 56 41 55.5 41 54Z' fill='#3f7f5e' stroke='#23513a' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<ellipse cx='47' cy='38' rx='8' ry='2' fill='#b5753f' stroke='#23513a' stroke-width='1.2'/>" +
            "<g fill='none' stroke='#d6ccbd' stroke-width='1.6' stroke-linecap='round'><path d='M44 33C44 30 46 30 46 27'/><path d='M50 33C50 30 52 30 52 27'/></g>",
        ["gear_3"] =
            // a polished brass flask
            "<path d='M20 18H44C48 18 51 21 51 25V51C51 54 48 57 45 57H19C16 57 13 54 13 51V25C13 21 16 18 20 18Z' fill='#d9ab45' stroke='#8f6a1f' stroke-width='1.6'/>" +
            "<path d='M18 24C18 22 19 21.5 21 21.5H27' fill='none' stroke='#f6dc92' stroke-width='2.4' stroke-linecap='round'/>" +
            "<rect x='18' y='30' width='4' height='20' rx='2' fill='#f3d58a' opacity='0.7'/>" +
            "<rect x='22' y='32' width='20' height='13' rx='2' fill='#b98a2c' stroke='#8f6a1f' stroke-width='1.2'/>" +
            "<path d='M27 38.5H37M32 34.5V42.5' stroke='#f6dc92' stroke-width='1.4' stroke-linecap='round'/>" +
            "<rect x='26' y='12' width='12' height='7' rx='1.4' fill='#b98a2c' stroke='#8f6a1f' stroke-width='1.4'/>" +
            "<rect x='24' y='7' width='16' height='6' rx='2' fill='#3a2a1c' stroke='#1f160e' stroke-width='1.2'/>",

        // ---------------------------------------------------------------- shop extras
        ["gun"] =
            // the red, blue and yellow toy pistol
            "<path d='M8 20H50C53 20 55 22 55 25V28C55 31 53 33 50 33H8C6 33 5 31.5 5 29.5V23.5C5 21.5 6 20 8 20Z' fill='#df4a35' stroke='#8f2c1f' stroke-width='1.6'/>" +
            "<rect x='53' y='22' width='6' height='9' rx='1.6' fill='#f7c948' stroke='#a07d1f' stroke-width='1.2'/>" +
            "<path d='M10 26H44' stroke='#f08a76' stroke-width='1.6' stroke-linecap='round'/>" +
            "<path d='M13 33H29L24 54C23.6 55.6 22.5 56 21 56H14C12 56 11 55 11.4 53.4Z' fill='#2f79c9' stroke='#1d4f86' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<path d='M15 40H24M14 45H23M13 50H22' stroke='#5b9be0' stroke-width='1.4' stroke-linecap='round'/>" +
            "<path d='M30 33C30 40 33 42 37 42' fill='none' stroke='#2b2f35' stroke-width='2.4' stroke-linecap='round'/><path d='M32 33L33 38' stroke='#f7c948' stroke-width='2.6' stroke-linecap='round'/>" +
            "<rect x='9' y='16' width='6' height='5' rx='1' fill='#f7c948'/>",
        ["cart_key"] =
            "<circle cx='18' cy='20' r='9' fill='none' stroke='#9aa3ad' stroke-width='2.6'/>" +
            "<circle cx='17' cy='38' r='9' fill='#f4f1e8' stroke='#b9b2a2' stroke-width='1.4'/>" +
            "<g fill='#d8d1c0'><circle cx='14' cy='35' r='1.2'/><circle cx='19' cy='34' r='1.2'/><circle cx='21' cy='39' r='1.2'/><circle cx='15' cy='40' r='1.2'/><circle cx='18' cy='43' r='1.1'/><circle cx='12.5' cy='38.5' r='1'/></g>" +
            "<path d='M17 29V29.5' stroke='#9aa3ad' stroke-width='2'/>" +
            "<g transform='rotate(35 34 22)'><rect x='22' y='14' width='15' height='16' rx='5' fill='#2b2f35' stroke='#15181a' stroke-width='1.4'/>" +
            "<circle cx='27' cy='22' r='2.2' fill='#9aa3ad'/>" +
            "<path d='M37 19H58V25H55V28H52V25H49V29H46V25H37Z' fill='#d6dbe0' stroke='#7c858f' stroke-width='1.2' stroke-linejoin='round'/></g>",
        ["boat_key"] =
            "<circle cx='18' cy='20' r='9' fill='none' stroke='#9aa3ad' stroke-width='2.6'/>" +
            // an orange float on the ring, so it does not sink
            "<rect x='9' y='31' width='16' height='24' rx='8' fill='#f08a2c' stroke='#a8561a' stroke-width='1.6'/>" +
            "<path d='M9.6 39H24.4M9.6 47H24.4' stroke='#f4f1e8' stroke-width='3'/>" +
            "<path d='M17 29V31.5' stroke='#9aa3ad' stroke-width='2'/>" +
            "<g transform='rotate(35 34 22)'><rect x='22' y='14' width='15' height='16' rx='5' fill='#1f4f7a' stroke='#123150' stroke-width='1.4'/>" +
            "<circle cx='27' cy='22' r='2.2' fill='#9aa3ad'/>" +
            "<path d='M37 19H58V25H55V28H52V25H49V29H46V25H37Z' fill='#d9ab45' stroke='#8f6a1f' stroke-width='1.2' stroke-linejoin='round'/></g>",
        ["bow"] =
            "<path d='M18 6C34 12 42 22 42 32C42 42 34 52 18 58' fill='none' stroke='#7a4a26' stroke-width='4.4' stroke-linecap='round'/>" +
            "<path d='M18 6C34 12 42 22 42 32C42 42 34 52 18 58' fill='none' stroke='#b07a44' stroke-width='1.4' stroke-linecap='round'/>" +
            "<path d='M40 27V37' stroke='#2b2f35' stroke-width='5.4' stroke-linecap='round'/>" +
            "<path d='M18.5 7L18.5 57' stroke='#eae4d6' stroke-width='1.2'/>" +
            "<path d='M8 32H56' stroke='#a8865a' stroke-width='2'/><path d='M56 32L50 28.5V35.5Z' fill='#9aa3ad' stroke='#5f6771' stroke-width='1'/>" +
            "<path d='M8 32L4 28M8 32L4 36M12 32L8 28M12 32L8 36' stroke='#d94b3e' stroke-width='2' stroke-linecap='round'/>",
        ["flags"] =
            "<g stroke='#6b5a3c' stroke-width='2' stroke-linecap='round'><path d='M14 16V56'/><path d='M32 10V56'/><path d='M50 18V56'/></g>" +
            "<path d='M15 16L29 21L15 26Z' fill='#e0503c' stroke='#9a2f1f' stroke-width='1.2' stroke-linejoin='round'/>" +
            "<path d='M33 10L47 15L33 20Z' fill='#f2c230' stroke='#a07d1f' stroke-width='1.2' stroke-linejoin='round'/>" +
            "<path d='M51 18L63 22.5L51 27Z' fill='#3e8fd6' stroke='#225c91' stroke-width='1.2' stroke-linejoin='round'/>" +
            "<ellipse cx='32' cy='56' rx='26' ry='3' fill='#d9c08a' opacity='0.7'/>",
        ["bucket"] =
            // a red bucket and a yellow spade
            "<path d='M12 26L40 26' stroke='#6b6f75' stroke-width='1.6'/>" +
            "<path d='M12 26C12 12 40 12 40 26' fill='none' stroke='#8a9096' stroke-width='2'/>" +
            "<path d='M10 26H42L38 56C37.8 57.5 36.8 58 35 58H17C15.2 58 14.2 57.5 14 56Z' fill='#e0503c' stroke='#9a2f1f' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<ellipse cx='26' cy='26' rx='16' ry='3.4' fill='#d9c08a' stroke='#9a2f1f' stroke-width='1.6'/>" +
            "<path d='M13 34H39' stroke='#f08a76' stroke-width='2.4'/>" +
            "<path d='M52 8V38' stroke='#e6a91e' stroke-width='3.4' stroke-linecap='round'/><rect x='47' y='5' width='10' height='5' rx='2.5' fill='#e6a91e'/>" +
            "<path d='M45 38H59L57 50C56 54 54 56 52 56C50 56 48 54 47 50Z' fill='#f2c230' stroke='#a07d1f' stroke-width='1.6' stroke-linejoin='round'/>",
        ["rod"] =
            "<path d='M8 56L54 8' stroke='#5a3a24' stroke-width='2.6' stroke-linecap='round'/>" +
            "<path d='M8 56L20 43.5' stroke='#2b2f35' stroke-width='5' stroke-linecap='round'/>" +
            "<circle cx='23' cy='45' r='5.4' fill='#c9ced4' stroke='#5f6771' stroke-width='1.4'/><circle cx='23' cy='45' r='1.8' fill='#5f6771'/>" +
            "<path d='M23 45L27 49' stroke='#2b2f35' stroke-width='2' stroke-linecap='round'/>" +
            "<g fill='none' stroke='#9aa3ad' stroke-width='1.2'><circle cx='32' cy='31.5' r='1.4'/><circle cx='42' cy='21' r='1.2'/></g>" +
            "<path d='M54 8C57 18 56 30 52 40' fill='none' stroke='#e4e8ec' stroke-width='1'/>" +
            "<path d='M52 40V42' stroke='#e4e8ec' stroke-width='1'/>" +
            "<circle cx='52' cy='46' r='4' fill='#f4f1e8' stroke='#6b6f75' stroke-width='1'/><path d='M48 46A4 4 0 0 1 56 46Z' fill='#e0503c'/>" +
            "<path d='M43 53C46 51.5 49 54.5 52 53C55 51.5 58 54.5 61 53' fill='none' stroke='#5b9fd0' stroke-width='1.8' stroke-linecap='round'/>",
        ["dive"] =
            // the mask and snorkel, with a fin behind
            "<path d='M36 30L52 10C55 7 60 9 59 13L50 40C49 43 46 44 43 42Z' fill='#2f8fd0' stroke='#1d5f8f' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<path d='M42 30L54 13M46 33L56 17' stroke='#6cb8e8' stroke-width='1.6' stroke-linecap='round'/>" +
            "<path d='M8 22C14 20 30 20 38 23' fill='none' stroke='#2b2f35' stroke-width='3' stroke-linecap='round'/>" +
            "<path d='M8 30C8 26 10 24 14 24H34C38 24 40 26 40 30V36C40 40 38 42 34 42H27L24 38L21 42H14C10 42 8 40 8 36Z' fill='#2b2f35' stroke='#15181a' stroke-width='1.4' stroke-linejoin='round'/>" +
            "<path d='M12 31C12 29 13 28 15 28H22V38H15C13 38 12 37 12 35Z' fill='#8fcbe8'/><path d='M26 28H33C35 28 36 29 36 31V35C36 37 35 38 33 38H26Z' fill='#8fcbe8'/>" +
            "<path d='M14 30L18 30' stroke='#e9f7ff' stroke-width='1.4' stroke-linecap='round'/>" +
            "<path d='M6 52V20C6 17 8 15 10 15' fill='none' stroke='#f2c230' stroke-width='3.4' stroke-linecap='round'/>" +
            "<path d='M6 52C6 55 9 56 12 55' fill='none' stroke='#f2c230' stroke-width='3.4' stroke-linecap='round'/><rect x='3' y='12' width='7' height='4' rx='1.5' fill='#e0503c'/>",
        ["net"] =
            "<path d='M8 58L30 34' stroke='#a86b3c' stroke-width='3.4' stroke-linecap='round'/><path d='M8 58L15 50.4' stroke='#2b2f35' stroke-width='4.6' stroke-linecap='round'/>" +
            "<path d='M30 34C30 46 40 56 48 54C54 52 56 44 52 36' fill='#e9e3d2' opacity='0.45'/>" +
            "<g fill='none' stroke='#b9b2a2' stroke-width='1'><path d='M33 34C34 44 41 52 47 52'/><path d='M38 30C40 40 45 48 50 49'/><path d='M44 28C46 36 50 42 53 43'/>" +
            "<path d='M31 39C38 38 45 35 52 30'/><path d='M34 46C41 44 48 41 54 37'/><path d='M40 52C46 49 51 46 54 43'/></g>" +
            "<ellipse cx='42' cy='28' rx='13' ry='8' transform='rotate(-32 42 28)' fill='none' stroke='#6b6f75' stroke-width='2.6'/>",
        ["bone"] = bone("#f1e9d6", "#b5a684"),

        // ---------------------------------------------------------------- camp decor
        ["decor_rug"] =
            "<path d='M8 22L56 22L60 44L4 44Z' fill='#c9563c' stroke='#8a3522' stroke-width='1.6' stroke-linejoin='round'/>" +
            "<path d='M7 27H57M6 32.5H58M5.2 38.5H58.8' stroke='#f2c230' stroke-width='2.6'/>" +
            "<path d='M6.6 30H57.4M5.6 35.5H58.4' stroke='#3e6f8f' stroke-width='1.6'/>" +
            "<g stroke='#e9dcc0' stroke-width='1.2' stroke-linecap='round'><path d='M10 44V49M16 44V49M22 44V49M28 44V49M34 44V49M40 44V49M46 44V49M52 44V49M58 44V49'/>" +
            "<path d='M12 22V17M18 22V17M24 22V17M30 22V17M36 22V17M42 22V17M48 22V17M54 22V17'/></g>",
        ["decor_sign"] =
            "<path d='M32 30V60' stroke='#7a4a26' stroke-width='4.4' stroke-linecap='round'/>" +
            "<path d='M6 14H54L60 24L54 34H6Z' fill='#c48a52' stroke='#6b3f1f' stroke-width='1.8' stroke-linejoin='round'/>" +
            "<path d='M8 20H52M8 28H52' stroke='#a8703c' stroke-width='1' opacity='0.8'/>" +
            "<path d='M12 22H40M12 26.5H32' stroke='#f4f1e8' stroke-width='2.4' stroke-linecap='round'/>" +
            "<circle cx='9.5' cy='17.5' r='1.1' fill='#4a2e1c'/><circle cx='9.5' cy='30.5' r='1.1' fill='#4a2e1c'/>",
        ["decor_lights"] =
            "<g stroke='#7a4a26' stroke-width='3' stroke-linecap='round'><path d='M6 14V60'/><path d='M58 14V60'/></g>" +
            "<path d='M6 16C20 34 44 34 58 16' fill='none' stroke='#3a3f45' stroke-width='1.2'/>" +
            "<g fill='#ffd86b' stroke='#b88a1f' stroke-width='0.8'><circle cx='13' cy='25' r='3'/><circle cx='22' cy='29.4' r='3'/><circle cx='32' cy='30.6' r='3'/><circle cx='42' cy='29.4' r='3'/><circle cx='51' cy='25' r='3'/></g>" +
            "<g fill='#ffe9a8' opacity='0.45'><circle cx='13' cy='25' r='6'/><circle cx='22' cy='29.4' r='6'/><circle cx='32' cy='30.6' r='6'/><circle cx='42' cy='29.4' r='6'/><circle cx='51' cy='25' r='6'/></g>",
        ["decor_cooler"] =
            "<path d='M8 28H56V52C56 54 54.5 55.5 52.5 55.5H11.5C9.5 55.5 8 54 8 52Z' fill='#e0503c' stroke='#9a2f1f' stroke-width='1.6'/>" +
            "<path d='M8 28V24C8 22 9.5 20.5 11.5 20.5H52.5C54.5 20.5 56 22 56 24V28Z' fill='#f4f1e8' stroke='#9a2f1f' stroke-width='1.6'/>" +
            "<path d='M24 20.5V16C24 14.5 25 13.5 26.5 13.5H37.5C39 13.5 40 14.5 40 16V20.5' fill='none' stroke='#f4f1e8' stroke-width='3'/>" +
            "<rect x='8' y='40' width='48' height='4' fill='#f4f1e8'/><rect x='12' y='31' width='14' height='5' rx='1.5' fill='#f08a76'/>",
        ["decor_palms"] =
            "<path d='M31 52C32 42 30 32 33 22' fill='none' stroke='#8a6440' stroke-width='3.6' stroke-linecap='round'/>" +
            "<g fill='#3f9a4a' stroke='#26682f' stroke-width='1.2' stroke-linejoin='round'>" +
            "<path d='M33 22C26 14 16 14 8 20C17 18 25 20 33 22Z'/><path d='M33 22C40 13 50 13 58 19C49 17 41 19 33 22Z'/>" +
            "<path d='M33 22C28 22 20 28 18 36C23 30 28 26 33 22Z'/><path d='M33 22C38 22 46 28 48 36C43 30 38 26 33 22Z'/><path d='M33 22C31 15 34 8 40 5C37 11 35 16 33 22Z'/></g>" +
            "<path d='M18 48H46L42 61H22Z' fill='#c46a3c' stroke='#8a4422' stroke-width='1.6' stroke-linejoin='round'/><rect x='16' y='45' width='32' height='5' rx='1.5' fill='#d47d4c' stroke='#8a4422' stroke-width='1.4'/>",
        ["decor_umbrella"] =
            "<path d='M28 16V58' stroke='#ececec' stroke-width='2.4' stroke-linecap='round'/>" +
            "<path d='M4 24C8 10 48 10 52 24Z' fill='#f4f1e8' stroke='#9a2f1f' stroke-width='1.4' stroke-linejoin='round'/>" +
            "<path d='M4 24C8 10 16 11 18 11.5C14 14 12 18 13.5 24Z' fill='#e0503c'/><path d='M28 10C24 13 22 18 23 24H33C34 18 32 13 28 10Z' fill='#e0503c'/>" +
            "<path d='M38 11.5C40 11 48 10 52 24H42.5C44 18 42 14 38 11.5Z' fill='#e0503c'/>" +
            "<path d='M32 50L60 44M34 50L38 58M56 45L58 56' stroke='#7a4a26' stroke-width='2' stroke-linecap='round'/>" +
            "<path d='M32 49L48 45.8L60 36' fill='none' stroke='#3e8fd6' stroke-width='3.6' stroke-linecap='round' stroke-linejoin='round'/>",
        ["decor_surf"] =
            "<path d='M6 52H58M10 52V58M54 52V58' stroke='#9a7650' stroke-width='3' stroke-linecap='round'/>" +
            "<path d='M8 36H56' stroke='#9a7650' stroke-width='2.4' stroke-linecap='round'/>" +
            "<path d='M16 52C12 40 12 20 18 6C24 20 24 40 20 52Z' fill='#f2c230' stroke='#a07d1f' stroke-width='1.4'/><path d='M18 10V50' stroke='#e0503c' stroke-width='1.6'/>" +
            "<path d='M30 52C26 42 26 24 32 10C38 24 38 42 34 52Z' fill='#3e8fd6' stroke='#225c91' stroke-width='1.4'/><path d='M27.5 34H36.5' stroke='#f4f1e8' stroke-width='3'/>" +
            "<path d='M44 52C40 40 40 22 46 8C52 22 52 40 48 52Z' fill='#4fb38a' stroke='#2b7a5a' stroke-width='1.4'/><circle cx='46' cy='28' r='3' fill='#f4f1e8'/>",
        ["decor_tiki"] =
            "<g stroke='#a8865a' stroke-width='3.4' stroke-linecap='round'><path d='M18 28V60'/><path d='M46 28V60'/></g>" +
            "<g stroke='#7a5a36' stroke-width='1.2'><path d='M16 36H20M16 44H20M16 52H20M44 36H48M44 44H48M44 52H48'/></g>" +
            "<g fill='#8a6440' stroke='#5a3e24' stroke-width='1.2'><path d='M13 20H23L21 29H15Z'/><path d='M41 20H51L49 29H43Z'/></g>" +
            "<path d='M18 20C12 15 15 9 18 4C19 9 24 11 22 17C21 19 20 20 18 20Z' fill='#f08a2c'/><path d='M18 19C16 17 17 13 18.5 11C19 14 21 15 20 18Z' fill='#ffd86b'/>" +
            "<path d='M46 20C40 15 43 9 46 4C47 9 52 11 50 17C49 19 48 20 46 20Z' fill='#f08a2c'/><path d='M46 19C44 17 45 13 46.5 11C47 14 49 15 48 18Z' fill='#ffd86b'/>",

        // ---------------------------------------------------------------- the Marigold's gold
        ["gold_cart"] = cart("#e8b93a", "#efe3c4", "#d8a830") +
            "<path d='M50 34L51 36.4L53.4 37.4L51 38.4L50 40.8L49 38.4L46.6 37.4L49 36.4Z' fill='#fff6c8'/>",
        ["marigold_boat"] = boat("#f4f1e8", "#e0a92e", "#d9ab45",
            "<rect x='44' y='39' width='9' height='4' rx='0.8' fill='#2b2f35'/><path d='M45.5 41H51.5' stroke='#e0a92e' stroke-width='1.2'/>"),
        ["monster_skull"] =
            // the great skull from the sea cave, on its stone plinth
            "<rect x='6' y='50' width='52' height='9' rx='1.5' fill='#8b8680' stroke='#5e5a55' stroke-width='1.4'/>" +
            "<path d='M10 44C6 30 12 12 32 10C52 12 58 30 54 44C52 48 48 50 44 50H20C16 50 12 48 10 44Z' fill='#e8dfc8' stroke='#8a7e62' stroke-width='1.8' stroke-linejoin='round'/>" +
            "<path d='M14 40C14 36 18 33 24 33C27 33 28 35 27 38C26 41 22 43 18 43C15.5 43 14 42 14 40Z' fill='#2b2620'/>" +
            "<path d='M50 40C50 36 46 33 40 33C37 33 36 35 37 38C38 41 42 43 46 43C48.5 43 50 42 50 40Z' fill='#2b2620'/>" +
            "<path d='M30 40L32 44L34 40Z' fill='#2b2620'/>" +
            "<g fill='#f4efe0' stroke='#8a7e62' stroke-width='1'><path d='M20 50L22 56L24 50Z'/><path d='M27 50L29 57L31 50Z'/><path d='M33 50L35 57L37 50Z'/><path d='M40 50L42 56L44 50Z'/></g>" +
            "<path d='M20 20C24 16 30 15 34 16M40 24C44 24 47 26 48 28' fill='none' stroke='#b9ad90' stroke-width='1.2' stroke-linecap='round'/>",
        ["gold_bone"] = bone("#f0c24a", "#a8801e") +
            "<path d='M44 32L45 34.4L47.4 35.4L45 36.4L44 38.8L43 36.4L40.6 35.4L43 34.4Z' fill='#fff6c8'/>",
    };

    /// <summary>The picture of a thing the camp sells, as SVG, or null if it has none.</summary>
    public static string svg(string id) => id != null && ART.TryGetValue(id, out var body) ? OPEN + body + "</svg>" : null;
}
