// Callouts in the Fog (antes "DBD Overlay") - anotar los perks y addons del killer.
// Compilar con build.ps1 (csc de .NET Framework 4, C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DbdOverlay
{
    // ------------------------------------------------------------ idioma de la app (textos de la interfaz)
    // IdiomaApp=auto (el de Windows), es o en. Los textos van en el codigo: L("español", "english").
    static class Lang
    {
        public static bool En, FirstRun;
        public static string Setting = "auto";
        public static void Init(string v)
        {
            Setting = (v ?? "auto").Trim().ToLowerInvariant();
            if (Setting.StartsWith("es")) En = false;
            else if (Setting.StartsWith("en")) En = true;
            else { Setting = "auto"; En = !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase); }
        }
        public static string L(string es, string en) { return En ? en : es; }
        // resultados que no anotaron nada (para no mostrarlos como logro ni mandarlos a la sala)
        public static bool Fail(string w)
        {
            if (string.IsNullOrEmpty(w)) return true;
            return w.StartsWith("no ") || w.StartsWith("didn't") || w.StartsWith("nothing") || w.StartsWith("couldn't") || w.StartsWith("there's no") || w.StartsWith("there are no");
        }
        public static bool Already(string w) { return w.Contains("ya estaba") || w.Contains("already there"); }
    }

    // ------------------------------------------------------------ config
    class Cfg
    {
        public Dictionary<string, string> V = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Get(string k, string d)
        {
            string v;
            return V.TryGetValue(k, out v) && v.Trim().Length > 0 ? v.Trim() : d;
        }
        public int GetInt(string k, int d) { int r; return int.TryParse(Get(k, ""), out r) ? r : d; }
        public double GetD(string k, double d)
        {
            double r;
            return double.TryParse(Get(k, "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : d;
        }

        public const string Default =
@"# Callouts in the Fog - configuracion (se lee al abrir el programa)

# Tecla que hay que MANTENER apretada mientras hablas.
# Ejemplos: Alt, AltIzq, AltDer, F8, V, XButton1 (boton lateral de atras del mouse), XButton2, Capital (Bloq Mayus)
Tecla=Shift+V

# Tecla para borrar todo: hay que MANTENERLA 1 segundo. Despues, un toque rapido la deshace (por unos segundos).
# (Por voz no se puede borrar todo, para que Whisper no lo haga sin querer.)
TeclaLimpiar=Shift+G

# Tecla para ver la ayuda con los comandos (mientras se mantiene apretada)
TeclaAyuda=Shift+H

# Combinacion para abrir el cuadro de escribir (en vez de hablar)
TeclaEscribir=Shift+T

# Las teclas solo funcionan con DBD en primer plano (si/no). Todas se cambian desde el menu del icono: Teclas
TeclasSoloEnDBD=si

# Reconocimiento de voz (si/no). Apagado no corre Whisper ni se usa el microfono
Voz=no
EscucharDiscord=si
SinTecla=no

# Microfono: vacio = el predeterminado de Windows. Si no, el principio del nombre como lo muestra Windows,
# por ejemplo: Microfono=Micrófono (NVIDIA Broadcast)
Microfono=

# Mostrar los nombres de perks/addons al lado del rombo (si/no). Tambien desde el menu del icono o diciendo ""mostrar nombres""
Nombres=no
# Tecla opcional para mostrar/ocultar nombres (vacio = ninguna)
TeclaNombres=Shift+M
# Tamano de cada perk en pixeles
TamanoPerk=72
# Tamano del nombre del killer y escala de todo el widget (%)
TamanoKiller=12
Escala=100

# Posicion: arriba-derecha, arriba-izquierda, abajo-derecha, abajo-izquierda
Posicion=arriba-derecha
MargenX=30
MargenY=140
TamanoLetra=14
Opacidad=0.85

# Sala compartida con amigos (se maneja desde el menu del icono)
# SalaAuto=si: todos los de tu grupo de DBD en Steam ven lo mismo, sin codigo
SalaAuto=si
# Sala = codigo de una sala manual (para quien no juega por Steam); vacio = usar la automatica
Sala=
Nombre=

# Whisper (reconocimiento de voz). Vacio = lo busca solo (carpeta whisper\ del programa).
# Si no esta, la app funciona en modo ""solo ver"". Se instala desde el menu del icono: ""Instalar voz"".
Whisper=
Modelo=
Puerto=8765
";

        // cambia (o agrega) una linea clave=valor conservando el resto del archivo
        public static void Set(string path, string key, string value)
        {
            try
            {
                var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
                int idx = lines.FindIndex(l => Regex.IsMatch(l, "^\\s*" + Regex.Escape(key) + "\\s*=", RegexOptions.IgnoreCase));
                if (idx >= 0) lines[idx] = key + "=" + value; else lines.Add(key + "=" + value);
                File.WriteAllLines(path, lines, new UTF8Encoding(true));
            }
            catch { }
        }

        public static Cfg Load(string path)
        {
            if (!File.Exists(path)) File.WriteAllText(path, Default, new UTF8Encoding(true));
            var c = new Cfg();
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith(";")) continue;
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                c.V[l.Substring(0, i).Trim()] = Environment.ExpandEnvironmentVariables(l.Substring(i + 1).Trim());
            }
            return c;
        }
    }

    // ------------------------------------------------------------ texto
    static class Txt
    {
        public static string Norm(string s)
        {
            if (s == null) return "";
            s = WebUtility.HtmlDecode(s).ToLowerInvariant().Replace("&", " and ")
                .Replace("'", "").Replace("’", "").Replace("‘", "").Replace("`", "");
            var f = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in f)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }
            return Regex.Replace(sb.ToString(), " +", " ").Trim();
        }

        // "fonetica" aproximada para nombres en ingles dichos con acento en espanol
        public static string Phon(string n)
        {
            string s = n.Replace(" ", "");
            s = s.Replace("ph", "f").Replace("th", "t").Replace("sh", "s").Replace("ck", "k").Replace("qu", "k").Replace("q", "k");
            s = Regex.Replace(s, "c(?=[eiy])", "s");
            s = s.Replace("c", "k").Replace("z", "s").Replace("v", "b").Replace("w", "u").Replace("y", "i")
                 .Replace("x", "ks").Replace("h", "").Replace("ee", "i").Replace("oo", "u");
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (sb.Length > 0 && sb[sb.Length - 1] == ch) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        public static string Skel(string p)
        {
            var sb = new StringBuilder();
            foreach (char ch in p) if ("aeiou".IndexOf(ch) < 0) sb.Append(ch);
            return sb.ToString();
        }

        public static int Lev(string a, string b)
        {
            var d = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) d[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int prev = d[0]; d[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int tmp = d[j];
                    d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                    prev = tmp;
                }
            }
            return d[b.Length];
        }

        // ---- palabras "de contenido" para comparar lo que hace un addon con su descripcion
        static readonly HashSet<string> Stop = new HashSet<string>(
            ("de la el los las un una unos unas que y e o u a al del en con por para se su sus le les lo es son sea da dan " +
             "hace hacen tiene tienen cuando mientras durante segundo segundos como muy este esta esto estos estas tras sin ese eso " +
             "no si ya cada todo toda todos todas otro otra killer asesino addon addons add on accesorio the of and to it").Split(' '));

        static readonly string[,] Syn = {
            {"rapid","veloc"},{"veloc","veloc"},{"veloz","veloc"},{"aceler","veloc"},
            {"lent","lent"},{"ralent","lent"},
            {"chic","reduc"},{"peque","reduc"},{"menor","reduc"},{"menos","reduc"},{"reduc","reduc"},{"dismin","reduc"},
            {"mayor","aument"},{"grand","aument"},{"aument","aument"},{"increm","aument"},
            {"radius","radio"},{"radio","radio"},
            {"undetect","indet"},{"indetect","indet"},
            {"hach","destr"},{"hatchet","destr"},{"destral","destr"},
            {"haste","celer"},{"celer","celer"},
            {"blind","ceguer"},{"ceguer","ceguer"},{"cieg","ceguer"},{"ciega","ceguer"},
            {"expos","expuest"},{"expuest","expuest"},
            {"mangl","lacer"},{"lacera","lacer"},
            {"hemorr","hemorr"},{"sangr","hemorr"},{"desangr","hemorr"},{"bleed","hemorr"},
            {"exhaust","agot"},{"agot","agot"},
            {"hinder","obstac"},{"obstac","obstac"},
            {"oblivi","ajeno"},{"ajen","ajeno"},
            {"incapac","incapac"},
            {"instadown","agoniz"},{"insta","agoniz"},{"oneshot","agoniz"},{"agoni","agoniz"},{"tumb","agoniz"},{"suelo","agoniz"},
            {"blink","parpad"},{"parpad","parpad"},{"speed","veloc"},{"cooldown","recarg"},
            {"recarg","recarg"},{"reload","recarg"},
            {"lanz","lanz"},{"tira","lanz"},{"throw","lanz"},
            {"tramp","tramp"},{"trap","tramp"},
            {"aura","aura"},{"terror","terror"},
            {"tiemp","tiemp"},{"tard","tiemp"},{"cuest","tiemp"},{"dific","tiemp"},
            // jerga de jugador -> palabras del juego (en espanol de Espana y en ingles)
            {"proyec","parpad"},{"teletrans","parpad"},
            {"invis","ocult"},{"ocult","ocult"},{"cloak","ocult"},{"camufl","ocult"},{"uncloak","ocult"},
            {"campan","campa"},{"bell","campa"},
            {"silen","silen"},{"mute","silen"},{"muffl","silen"},{"callad","silen"},{"amortig","silen"},
            {"ruid","sonid"},{"sonid","sonid"},{"sound","sonid"},
            {"escap","huir"},{"huir","huir"},{"huye","huir"},{"zaf","huir"},
            {"increas","aument"},{"decreas","reduc"},{"faster","veloc"},{"slower","lent"},{"longer","aument"},
            {"durac","durac"},{"duration","durac"},{"dura","durac"},
            {"maxim","maxim"},{"cantid","maxim"},{"numer","maxim"},{"carried","maxim"},
            {"wallhack","aura"},{"cura","curar"},{"heal","curar"},
            {"motosierr","motos"},{"chainsaw","motos"},{"sierra","motos"},
            {"generad","gener"},{"gene","gener"},{"gens","gener"}
        };

        // palabras exactas (las cortas no se pueden comparar por el principio: "mas" no es "mascara")
        static readonly Dictionary<string, string> SynExact = new Dictionary<string, string> {
            { "mas", "aument" }, { "more", "aument" }, { "gen", "gener" }, { "less", "reduc" }, { "fewer", "reduc" },
            { "cargas", "maxim" }, { "token", "maxim" }, { "tokens", "maxim" } };

        public static List<string> Tokens(string text)
        {
            var r = new List<string>();
            string all = Norm(text);
            foreach (var w in all.Split(' '))
            {
                string ex;
                if (SynExact.TryGetValue(w, out ex)) { r.Add(ex); continue; }
                if (w.Length < 2 || Stop.Contains(w)) continue;
                string s = null;
                for (int i = 0; i < Syn.GetLength(0); i++)
                    if (w.StartsWith(Syn[i, 0])) { s = Syn[i, 1]; break; }
                if (s == null) s = w.Length > 5 ? w.Substring(0, 5) : w;
                if (s.Length < 3 && !char.IsDigit(s[0])) continue;
                r.Add(s);
            }
            // "reduce el tiempo de X" = "X mas rapido"
            if (r.Contains("reduc") && r.Contains("tiemp")) r.Add("veloc");
            // "que no suene", "que no se escuche", "no hace ruido" = silencio
            if (Regex.IsMatch(all, @"\bno (?:se )?(?:suen|escuch|oy|oig|hac\w* ruido)")) r.Add("silen");
            // "mas hachas", "mas trampas", "mas blinks" = cuantas puede tener
            if (Regex.IsMatch(all, @"\bmas (?:hachas|destrales|trampas|blinks|cuchillos|cargas)\b")) r.Add("maxim");
            return r;
        }

        // clave para cruzar nombres con los archivos de la wiki
        public static string FileKey(string s)
        {
            s = Regex.Replace(s, @"^(T_UI_|T_)?icons?(Perks|Addon|Favors)_|\.png$", "", RegexOptions.IgnoreCase); // formato viejo (IconPerks_) y nuevo (T_UI_iconsPerks_)
            var f = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in f) if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            return sb.ToString();
        }

        public static double Ratio(string a, string b)
        {
            int m = Math.Max(a.Length, b.Length);
            if (m == 0) return 1;
            return 1.0 - (double)Lev(a, b) / m;
        }
    }

    // ------------------------------------------------------------ catalogo
    enum Kind { Killer, Perk, Addon, Offering }

    class Key
    {
        public string N, P, S; public string[] W;
        public Key(string norm) { N = norm; P = Txt.Phon(norm); S = Txt.Skel(P); W = norm.Split(' '); }
    }

    class Entry
    {
        public Kind K; public string Name; public string Killer; public string Rarity;
        public bool Secret, Synth; // Synth: ofrenda "de un tipo" armada por la app (de mapa, niebla, ganchos, cofres)                                  // ofrenda que el juego muestra como secreta (el dorso de la carta), sin las de mapa
        public string Icon;                                  // archivo en deadbydaylight.wiki.gg/images/
        public HashSet<string> Desc = new HashSet<string>(); // palabras de la descripcion en espanol
        public string NameEs, Info, InfoEn;                  // nombre en espanol y descripciones (espanol / ingles) para leer (hover)
        public string Owner;                                 // perks: el personaje dueño (vacio = perk general)
        public string Action, ActionTag;                     // solo en el cuadro de escribir: acciones de sala ("salir", "nueva", "entrar:codigo")
        public List<Key> Keys = new List<Key>();
        public void AddKey(string s)
        {
            var n = Txt.Norm(s);
            if (n.Length == 0 || Keys.Any(k => k.N == n)) return;
            Keys.Add(new Key(n));
        }
    }

    class Catalog
    {
        public List<Entry> Killers = new List<Entry>(), Perks = new List<Entry>(), Addons = new List<Entry>(), Offerings = new List<Entry>();
        public List<Entry> Maps = new List<Entry>();                                   // mapas (solo para elegir y ver el plano)
        public Dictionary<string, List<string>> MapFiles = new Dictionary<string, List<string>>(); // mapa -> plano(s) de la wiki
        public string Info = "";

        static readonly string[][] KillerAliases = {
            new[]{"Trapper","trampero"}, new[]{"Wraith","espectro"}, new[]{"Hillbilly","billy"},
            new[]{"Nurse","enfermera","sally"}, new[]{"Hag","bruja"}, new[]{"Shape","myers","michael myers","michael"},
            new[]{"Doctor","doc"}, new[]{"Huntress","cazadora"}, new[]{"Cannibal","bubba","leatherface","canibal"},
            new[]{"Nightmare","freddy","pesadilla"}, new[]{"Pig","cerda","chancha","amanda"}, new[]{"Clown","payaso"},
            new[]{"Spirit","espiritu","rin"}, new[]{"Legion"}, new[]{"Plague","plaga"}, new[]{"Ghost Face","ghostface"},
            new[]{"Demogorgon","demo"}, new[]{"Deathslinger","slinger"},
            new[]{"Executioner","pyramid head","pyramid","cabeza de piramide","verdugo"}, new[]{"Twins","gemelos"},
            new[]{"Cenobite","pinhead"}, new[]{"Artist","artista"}, new[]{"Onryo","sadako"},
            new[]{"Mastermind","wesker"}, new[]{"Knight","caballero"}, new[]{"Skull Merchant","skull"},
            new[]{"Singularity","singularidad"}, new[]{"Xenomorph","alien","xeno"}, new[]{"Good Guy","chucky"},
            new[]{"Unknown","desconocido"}, new[]{"Dark Lord","dracula"}, new[]{"Ghoul","kaneki","caneki","kaneke","caneque","ken kaneki"},
            new[]{"Animatronic","springtrap","freddy fazbear"}, new[]{"First","henry","vecna"},
            new[]{"Slasher","jason","jason voorhees","voorhees","yeison"}
        };

        static readonly string[][] OfferingAliases = {
            new[]{"ebony memento mori","mori","mori de ebano","mori ebano","ebony mori","ebony"},
            new[]{"ivory memento mori","ivory mori","ivory","mori de marfil"},
            new[]{"cypress memento mori","cypress mori","cypress","mori de cipres"},
            new[]{"black ward","black word","ward negro"}
        };

        // perks de licencias que se fueron: quien no tiene el capitulo los ve con otro nombre (perk general). { viejo, nuevo }
        public static readonly string[][] Renames = {
            new[] { "Scourge Hook: Gift of Pain", "Scourge Hook: Weeping Wounds" }, new[] { "Hex: Plaything", "Hex: Fortune's Fool" },
            new[] { "Deadlock", "No Holds Barred" },                                                             // Cenobite, parche 9.0.0
            new[] { "Play With Your Food", "See How They Run" }, new[] { "Save the Best for Last", "Keep Them Waiting" },
            new[] { "Dying Light", "Cull the Weak" },                                                            // Shape, parche 9.4.0
            new[] { "Hex: Two Can Play", "Hex: Eye for an Eye" }, new[] { "Friends 'til the End", "One Track Mind" },
            new[] { "Batteries Included", "Gaining Steam" } };                                                   // Chucky, anunciado para el 24/11/2026
        static readonly string[][] PerkAliases = {
            new[]{"barbecue","bbq","barbacoa"}, new[]{"pop goes the weasel","pop","pgtw"},
            new[]{"no one escapes death","noed","no ed"}, new[]{"dead mans switch","dms"},
            new[]{"save the best for last","stbfl"}, new[]{"play with your food","pwyf"},
            new[]{"make your choice","myc"}, new[]{"surge","jolt","search","surch","serge"}, new[]{"thanatophobia","thana"},
            new[]{"monitor and abuse","monitor"}, new[]{"pain resonance","painless","pain res","pain reso"}
        };

        // ---- datos al dia: al abrir se mira /api/versions (menos de 1 KB) y solo se baja lo que cambio desde la ultima vez
        static readonly string[] Datasets = { "characters", "perks", "addons", "offerings", "maps" };
        static Dictionary<string, string> remoteVer, localVer;
        static bool fetchFailed;
        public static string GameVersion = "", DataNews = "";

        static Dictionary<string, string> ReadVer(string path)
        {
            var d = new Dictionary<string, string>();
            if (File.Exists(path))
                foreach (var l in File.ReadAllLines(path)) { int i = l.IndexOf('='); if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1); }
            return d;
        }

        static void CheckVersions(string dir)
        {
            localVer = ReadVer(Path.Combine(dir, "versiones.txt"));
            remoteVer = null; fetchFailed = false; DataNews = "";
            try
            {
                var js = new JavaScriptSerializer();
                var d = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(HttpBytes("https://dbd.tricky.lol/api/versions")));
                var r = new Dictionary<string, string>();
                foreach (var ds in Datasets)
                {
                    var x = d.ContainsKey(ds) ? d[ds] as Dictionary<string, object> : null;
                    if (x == null) continue;
                    r[ds] = S(x, "lastupdate");
                    if (r[ds].Length > 0) r[ds + ".version"] = S(x, "version");
                }
                if (r.Count > 0) remoteVer = r;
            }
            catch { } // sin internet: se usa lo guardado
            string v;
            if (remoteVer != null && remoteVer.TryGetValue("perks.version", out v)) GameVersion = v;
            else if (localVer.TryGetValue("perks.version", out v)) GameVersion = v;
            var changed = Datasets.Where(Changed).ToList();
            if (remoteVer != null && localVer.Count > 0 && changed.Count > 0)
                DataNews = Lang.L("datos del juego actualizados", "game data updated") + (GameVersion.Length > 0 ? " (" + GameVersion + ")" : "");
        }

        static bool Changed(string ds)
        {
            if (remoteVer == null) return false;
            string a, b;
            return !remoteVer.TryGetValue(ds, out a) || !localVer.TryGetValue(ds, out b) || a != b;
        }

        static void SaveVersions(string dir)
        {
            if (remoteVer == null || fetchFailed) return; // si algo no bajo, la proxima vez se reintenta
            File.WriteAllLines(Path.Combine(dir, "versiones.txt"), remoteVer.Select(kv => kv.Key + "=" + kv.Value));
        }

        static string Fetch(string dir, string file, string url)
        {
            string path = Path.Combine(dir, file);
            string ds = Regex.Match(url, @"/api/(\w+)").Groups[1].Value;
            // con la lista de versiones: bajar solo si cambio; sin ella (sin internet o caida): como antes, si tiene mas de 3 dias
            bool stale = !File.Exists(path) || (remoteVer != null ? Changed(ds) : (DateTime.Now - File.GetLastWriteTime(path)).TotalDays > 3);
            if (stale)
            {
                try
                {
                    string txt = Encoding.UTF8.GetString(HttpBytes(url));
                    if (txt.TrimStart().StartsWith("{")) File.WriteAllText(path, txt, Encoding.UTF8);
                    else fetchFailed = true;
                }
                catch { fetchFailed = true; /* sin internet: usamos el cache */ }
            }
            if (!File.Exists(path)) throw new Exception("No pude descargar " + url + " y no hay copia guardada.");
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public static byte[] HttpBytes(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "DbdOverlay/1.0 (personal overlay)";
            req.Timeout = 15000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = req.GetResponse())
            using (var ms = new MemoryStream())
            {
                resp.GetResponseStream().CopyTo(ms);
                return ms.ToArray();
            }
        }

        // lista de imagenes de la wiki (IconPerks_*, IconAddon_*), cacheada una semana
        // lista de mapas de hens333.com/callouts: la pagina guarda la lista en sus archivos .js
        // ({realm:"…",names:["…"],image:"Carpeta/Mapa.webp"}). Se guarda en hens_mapas.txt: "nombres|…<tab>imagen<tab>reino".
        public const string HensBase = "https://hens333.com/";
        public static byte[] WebpToPng(byte[] webp)
        {
            using (var ms = new MemoryStream(webp))
            using (var o = new MemoryStream())
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(dec.Frames[0]));
                enc.Save(o);
                return o.ToArray();
            }
        }
        // nombres que la app usaba antes de que hens rehiciera la pagina (noviembre 2026): se mantienen como nombre principal
        // para que el mapa compartido en la sala se entienda con las versiones anteriores de la app
        static readonly Dictionary<string, string> HensOldNames = new Dictionary<string, string> {
            { "rancidabattoir", "Rancid Abbatoir" }, { "theshatteredsquare", "Shattered Square" }, { "fathercampbellschapel", "Father Campbells Chapel" },
            { "freddyfazbearspizza", "Freddy Fazbears Pizza" }, { "ironworksofmisery", "Ironworks Of Misery" }, { "ironworksofmiseryii", "Ironworks Of Misery II" },
            { "rpdeastwing", "Police Station East Wing" }, { "rpdwestwing", "Police Station West Wing" },
            { "badhampreschool", "Preschool I|Preschool 1" }, { "badhampreschoolii", "Preschool II|Preschool 2" }, { "badhampreschooliii", "Preschool III|Preschool 3" },
            { "badhampreschooliv", "Preschool IIIV|Preschool IV|Preschool 4" }, { "badhampreschoolv", "Preschool V|Preschool 5" },
            { "tobalanding", "Toba Landing|Toba|TB" }, { "nostromowreckage", "Nostromo Wreckage|Nostromo|NB" } };

        // la pagina hens333.com/callouts/ (hecha con Astro) lista cada mapa por reino con sus imagenes en /_astro/, con un codigo
        // en el nombre que cambia cuando hens actualiza la web. Se usa la version "classic" (la de siempre, con reloj).
        // force: releer la pagina aunque la lista guardada sea nueva (la imagen guardada ya no existe).
        internal static List<string> HensMapList(string dir, bool force = false)
        {
            string path = Path.Combine(dir, "hens_mapas.txt");
            bool stale = force || !File.Exists(path) || (remoteVer != null ? Changed("maps") : (DateTime.Now - File.GetLastWriteTime(path)).TotalDays > 7);
            // lista del formato viejo (imagenes en img/dbd/callouts/, que ya no existen)
            if (!stale) try { stale = File.ReadAllLines(path, Encoding.UTF8).Any(l => { var f = l.Split('\t'); return f.Length >= 2 && !f[1].StartsWith("/_astro/"); }); } catch { stale = true; }
            if (stale)
            {
                try
                {
                    string html = Encoding.UTF8.GetString(HttpBytes(HensBase + "callouts/"));
                    var lines = new List<string>();
                    foreach (var sec in Regex.Split(html, @"<section class=""realm""").Skip(1))
                    {
                        var rm = Regex.Match(sec, @"class=""realm-name[^""]*""[^>]*>(?:<span[^>]*></span>)?([^<]+)");
                        string realm = rm.Success ? WebUtility.HtmlDecode(rm.Groups[1].Value).Trim() : "";
                        foreach (var li in Regex.Split(sec, @"<li data-map=").Skip(1))
                        {
                            var im = Regex.Match(li, @"<img src=""(/_astro/[^""]+)""[^>]*?data-style=""classic""");
                            if (!im.Success) im = Regex.Match(li, @"<img src=""(/_astro/[^""]+)""");
                            var nm = Regex.Match(li, @"class=""map-name""[^>]*>([^<]+)");
                            if (!im.Success || !nm.Success) continue;
                            string name = WebUtility.HtmlDecode(nm.Groups[1].Value).Trim(), old;
                            string names = HensOldNames.TryGetValue(Overlay.Bare(name), out old) ? old + "|" + name : name;
                            lines.Add(names + "\t" + im.Groups[1].Value + "\t" + realm);
                        }
                    }
                    if (lines.Count > 0) File.WriteAllLines(path, lines, Encoding.UTF8);
                }
                catch { }
            }
            return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
        }

        static List<string> WikiList(string dir, string file, string prefix)
        {
            string path = Path.Combine(dir, file);
            bool stale = !File.Exists(path) || (remoteVer != null ? Datasets.Any(Changed) : (DateTime.Now - File.GetLastWriteTime(path)).TotalDays > 7); // iconos nuevos: cuando cambian los datos
            if (stale)
            {
                try
                {
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var names = new List<string>();
                    // varios prefijos separados por | (la wiki usa "IconPerks_x" para lo viejo y "T_UI_iconsPerks_X" para lo nuevo)
                    foreach (var pre in prefix.Split('|'))
                    {
                        string cont = "";
                        for (int guard = 0; guard < 20; guard++)
                        {
                            string url = "https://deadbydaylight.wiki.gg/api.php?action=query&list=allimages&aiprefix=" + pre +
                                         "&ailimit=500&format=json" + (cont.Length > 0 ? "&aicontinue=" + Uri.EscapeDataString(cont) : "");
                            var d = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(HttpBytes(url)));
                            var q = (Dictionary<string, object>)d["query"];
                            foreach (Dictionary<string, object> i in (System.Collections.ArrayList)q["allimages"]) names.Add(i["name"].ToString());
                            object c;
                            if (d.TryGetValue("continue", out c)) cont = ((Dictionary<string, object>)c)["aicontinue"].ToString();
                            else break;
                        }
                    }
                    if (names.Count > 0) File.WriteAllLines(path, names, Encoding.UTF8);
                }
                catch { }
            }
            return File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
        }

        static Dictionary<string, string> KeyMap(List<string> files)
        {
            var m = new Dictionary<string, string>();
            foreach (var f in files) { var k = Txt.FileKey(f); if (k.Length > 0 && !m.ContainsKey(k)) m[k] = f; }
            return m;
        }

        static string FindIcon(Dictionary<string, string> m, params string[] keys)
        {
            foreach (var k in keys) { string f; if (k.Length > 0 && m.TryGetValue(k, out f)) return f; }
            // casi igual ("barbecueandchili" vs "barbecueandchilli")
            string best = null; double bs = 0;
            foreach (var k in keys)
            {
                if (k.Length < 6) continue;
                foreach (var kv in m)
                {
                    if (Math.Abs(kv.Key.Length - k.Length) > 3) continue;
                    double s = Txt.Ratio(k, kv.Key);
                    if (s > bs) { bs = s; best = kv.Value; }
                }
            }
            return bs >= 0.9 ? best : null;
        }

        static string StripHtml(string s)
        {
            return WebUtility.HtmlDecode(Regex.Replace(s ?? "", "<[^>]+>", " "));
        }

        // ---- descripcion legible: valores del nivel 3 en lugar de {Tunable…}, estados en espanol, sin HTML
        static readonly Dictionary<string, string> KeywordEs = new Dictionary<string, string> {
            { "haste", "Celeridad" }, { "hindered", "Obstaculizado" }, { "exposed", "Expuesto" }, { "exhausted", "Agotado" },
            { "blindness", "Ceguera" }, { "broken", "Destrozado" }, { "hemorrhage", "Hemorragia" }, { "mangled", "Mutilado" },
            { "oblivious", "Inconsciente" }, { "undetectable", "Indetectable" }, { "incapacitated", "Incapacitado" } };
        static readonly CultureInfo Es = new CultureInfo("es-AR");

        static string TunVal(Dictionary<string, object> tun, string name, bool es)
        {
            object o;
            if (tun == null || !tun.TryGetValue(name.ToLowerInvariant(), out o) || o == null) return "?";
            var arr = o as System.Collections.ArrayList;
            object v = arr != null ? (arr.Count > 0 ? arr[arr.Count - 1] : null) : o;
            if (v == null) return "?";
            double d;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d.ToString("0.##", es ? Es : CultureInfo.InvariantCulture) : v.ToString();
        }

        public static string NiceDesc(string html, Dictionary<string, object> tun, bool es = true)
        {
            string s = html ?? "";
            s = Regex.Replace(s, @"\{Tunable\.[^.}]+\.([^}]+)\}", m => TunVal(tun, m.Groups[1].Value, es));
            s = Regex.Replace(s, @"\{Keyword\.([A-Za-z]+)\}", m => { string v; return es && KeywordEs.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out v) ? v : char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value.Substring(1); });
            s = Regex.Replace(s, @"\{[^}]*\}", "");
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<li>", "\n• ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</?(ul|ol)>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @" *\n *", "\n");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            s = Regex.Replace(s, @"\n+•", "\n•").Trim(); // las viñetas pegadas a la frase que las presenta
            s = Regex.Replace(s, @"(?<=[a-zà-ÿ%)])\.(?=\p{Lu})", ". "); // "adicional.Permite" -> "adicional. Permite"
            return s;
        }

        // solo lo que hace (sin el texto de ambientacion del principio, que no dice nada del efecto)
        static string Effect(string html) { return StripHtml(EffectHtml(html)); }

        static bool IsFlavor(string s) { return !Regex.IsMatch(s, @"\d|<b>|\{|<li>"); }

        static readonly Regex GlossaryRx = new Regex(
            @"^(?:(?:El|La|Los|Las|The)\s+(?:estado\s+(?:de\s+)?)?)?(?:Hemorragia|Herida profunda|Laceración|Celeridad|Inconsciencia|Ceguera|Agotamiento|Agotado|Expuesto|Obstaculizado|Obstaculización|Mutilado|Mutilación|Destrozado|Quebrantado|Indetectable|Incapacitado|Incapacitación|Locura|Haemorrhage|Hemorrhage|Deep Wound|Haste|Oblivious|Blindness|Exhausted|Exhaustion|Exposed|Hindered|Mangled|Broken|Undetectable|Incapacitated|Madness)\s+(?:aumenta|reduce|impide|revierte|hace|hará|provoca|evita|ralentiza|increases|decreases|prevents|reverts|regresses|causes|makes|slows|lasts|dura)\b",
            RegexOptions.IgnoreCase);

        // la ambientacion va primero: o es un parrafo aparte, o termina en un punto pegado al efecto ("...campana.<b>Aumenta</b>...")
        public static string EffectHtml(string html)
        {
            var parts = Regex.Split(html ?? "", @"(?:<br\s*/?>\s*){2,}", RegexOptions.IgnoreCase).Where(p => p.Trim().Length > 0).ToList();
            if (parts.Count == 0) return "";
            if (parts.Count >= 2 && IsFlavor(parts[0])) parts.RemoveAt(0);
            else
            {
                var m = Regex.Match(parts[0], @"^(.*?[.!?…])(?=<b>|<i>|\p{Lu})");
                if (m.Success && m.Length < parts[0].Length && IsFlavor(m.Groups[1].Value)) parts[0] = parts[0].Substring(m.Length);
            }
            // glosario del final ("Hemorragia revierte el progreso de curacion…"): explica el estado en general, no el addon
            parts = parts.Where((p, i) => i == 0 || !GlossaryRx.IsMatch(StripHtml(p).Trim())).ToList();
            return string.Join("<br><br>", parts);
        }

        static Dictionary<string, object> Dict(Dictionary<string, object> d, string k)
        {
            object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null;
        }

        static string S(Dictionary<string, object> d, string k)
        {
            object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : "";
        }

        public static Catalog Load(string dir)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            Directory.CreateDirectory(dir);
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            CheckVersions(dir);
            var chars = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "killers.json", "https://dbd.tricky.lol/api/characters?role=killer"));
            var perks = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "perks.json", "https://dbd.tricky.lol/api/perks?role=killer"));
            var addons = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "addons.json", "https://dbd.tricky.lol/api/addons?role=killer"));
            Dictionary<string, object> addonsEs = null;
            try { addonsEs = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "addons_es.json", "https://dbd.tricky.lol/api/addons?role=killer&locale=es")); }
            catch { }
            Dictionary<string, object> perksEs = null;
            try { perksEs = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "perks_es.json", "https://dbd.tricky.lol/api/perks?role=killer&locale=es")); }
            catch { }
            var perkIcons = KeyMap(WikiList(dir, "wiki_perks3.txt", "IconPerks_|T_UI_iconsPerks_|IconsPerks_"));
            var addonIcons = KeyMap(WikiList(dir, "wiki_addons2.txt", "IconAddon_|T_UI_iconAddon_"));

            var c = new Catalog();
            var byItem = new Dictionary<string, string>();
            var charName = new Dictionary<string, string>();
            foreach (var kv in chars)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d == null) continue;
                string name = WebUtility.HtmlDecode(S(d, "name"));
                var e = new Entry { K = Kind.Killer, Name = name };
                string portrait = S(d, "image").Split('/').Last();
                // en el wiki algunos tienen otro nombre que en el juego
                portrait = Regex.Replace(portrait, "^T_UI_", "");
                string fix;
                if (Overlay.PortraitFix.TryGetValue(name, out fix)) portrait = fix;
                if (portrait.Length > 0) e.Icon = portrait;
                charName[kv.Key] = name;
                e.AddKey(name);
                string shortName = Regex.Replace(name, "^The ", "");
                e.AddKey(shortName);
                foreach (var al in KillerAliases)
                    if (Txt.Norm(al[0]) == Txt.Norm(shortName))
                        foreach (var a in al) e.AddKey(a);
                c.Killers.Add(e);
                string item = S(d, "item");
                if (item.Length > 0) byItem[item] = name;
            }
            foreach (var kv in perks)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d == null) continue;
                string name = WebUtility.HtmlDecode(S(d, "name"));
                var e = new Entry { K = Kind.Perk, Name = name };
                string owner; if (charName.TryGetValue(S(d, "character"), out owner)) e.Owner = owner;
                e.AddKey(name);
                int ci = name.IndexOf(':');
                if (ci > 0) e.AddKey(name.Substring(ci + 1));
                foreach (var rn in Renames)
                    if (HensBuilds.Loose(rn[0]) == HensBuilds.Loose(name))
                    {
                        e.AddKey(rn[1]);
                        int rc = rn[1].IndexOf(':');
                        if (rc > 0) e.AddKey(rn[1].Substring(rc + 1));
                    }
                string n = Txt.Norm(name);
                foreach (var al in PerkAliases)
                    if (n.Contains(al[0])) for (int i = 1; i < al.Length; i++) e.AddKey(al[i]);
                string bare = Regex.Replace(name, @"^(Hex|Scourge Hook|Boon|Invocation):\s*", "");
                e.Icon = FindIcon(perkIcons, Txt.FileKey(name), Txt.FileKey(Txt.Norm(name)), Txt.FileKey(bare), Txt.FileKey(S(d, "image").Split('/').Last()), "hex" + Txt.FileKey(bare));
                var pe = Dict(perksEs, kv.Key);
                if (pe != null) { e.NameEs = WebUtility.HtmlDecode(S(pe, "name")); e.Info = NiceDesc(S(pe, "description"), Dict(pe, "tunables") ?? Dict(d, "tunables")); }
                else e.Info = NiceDesc(S(d, "description"), Dict(d, "tunables"));
                e.InfoEn = NiceDesc(S(d, "description"), Dict(d, "tunables"), false);
                c.Perks.Add(e);
            }
            foreach (var kv in addons)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d == null) continue;
                string name = WebUtility.HtmlDecode(S(d, "name"));
                if (name.StartsWith("@#") || name.Length == 0) continue; // addons sin terminar en los datos del juego
                string killer = "";
                var parents = d.ContainsKey("parents") ? d["parents"] as System.Collections.ArrayList : null;
                if (parents != null && parents.Count > 0) byItem.TryGetValue(parents[0].ToString(), out killer);
                var e = new Entry { K = Kind.Addon, Name = name, Killer = killer ?? "", Rarity = S(d, "rarity") };
                e.AddKey(name);
                e.Icon = FindIcon(addonIcons, Txt.FileKey(name), Txt.FileKey(Txt.Norm(name)), Txt.FileKey(S(d, "image").Split('/').Last()));
                object eso;
                if (addonsEs != null && addonsEs.TryGetValue(kv.Key, out eso) && eso is Dictionary<string, object>)
                {
                    var de = (Dictionary<string, object>)eso;
                    string nameEs = WebUtility.HtmlDecode(S(de, "name"));
                    e.AddKey(nameEs);
                    foreach (var t in Txt.Tokens(nameEs + " " + Effect(S(de, "description")))) e.Desc.Add(t);
                    e.NameEs = nameEs; e.Info = NiceDesc(EffectHtml(S(de, "description")), null);
                }
                else e.Info = NiceDesc(EffectHtml(S(d, "description")), null);
                // tambien la descripcion en ingles: la jerga de los jugadores sale de ahi ("blink", "cloak", "bell")
                foreach (var t in Txt.Tokens(name + " " + Effect(S(d, "description")))) e.Desc.Add(t);
                e.InfoEn = NiceDesc(EffectHtml(S(d, "description")), null, false);
                c.Addons.Add(e);
            }
            // ofrendas que puede quemar el killer (las de killer y las compartidas; sin las retiradas)
            try
            {
                var offs = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "offerings.json", "https://dbd.tricky.lol/api/offerings"));
                Dictionary<string, object> offsEs = null;
                try { offsEs = js.Deserialize<Dictionary<string, object>>(Fetch(dir, "offerings_es.json", "https://dbd.tricky.lol/api/offerings?locale=es")); }
                catch { }
                var offIcons = KeyMap(WikiList(dir, "wiki_ofrendas3.txt", "IconFavors_|T_UI_iconsFavors_|IconsFavors_"));
                foreach (var kv in offs)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null || S(d, "role") == "survivor" || S(d, "retired") == "1") continue;
                    string name = WebUtility.HtmlDecode(S(d, "name"));
                    if (name.Length == 0 || name.StartsWith("@#")) continue;
                    name = Regex.Replace(name, @"\b([A-Z])([A-Z]+)\b", m => m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant()); // "TERRORMISU" -> "Terrormisu"
                    var e = new Entry { K = Kind.Offering, Name = name, Killer = "", Rarity = S(d, "rarity"),
                                        // secreta y que no sea de mapa ("...sent to Coldwind Farm"): el mapa se ve al cargar
                                        Secret = Regex.IsMatch(S(d, "description"), @"\bSecret\b") && !Regex.IsMatch(S(d, "description"), @"\bsent\b|take on the role", RegexOptions.IgnoreCase) };
                    e.AddKey(name);
                    foreach (var al in OfferingAliases)
                        if (Txt.Norm(name).Contains(al[0])) for (int i = 1; i < al.Length; i++) e.AddKey(al[i]);
                    e.Icon = FindIcon(offIcons, Txt.FileKey(name), Txt.FileKey(Txt.Norm(name)), Txt.FileKey(S(d, "image").Split('/').Last()),
                                      Txt.FileKey(Regex.Replace(S(d, "image").Split('/').Last(), "^icons?Favors_", "", RegexOptions.IgnoreCase)));
                    object eso;
                    string desc = StripHtml(S(d, "description"));
                    e.Info = NiceDesc(EffectHtml(S(d, "description")), null); e.InfoEn = NiceDesc(EffectHtml(S(d, "description")), null, false);
                    if (offsEs != null && offsEs.TryGetValue(kv.Key, out eso) && eso is Dictionary<string, object>)
                    {
                        var de = (Dictionary<string, object>)eso;
                        string nameEs = WebUtility.HtmlDecode(S(de, "name"));
                        e.AddKey(nameEs);
                        desc = nameEs + " " + StripHtml(S(de, "description"));
                        e.NameEs = nameEs; e.Info = NiceDesc(EffectHtml(S(de, "description")), null);
                    }
                    foreach (var t in Txt.Tokens(desc)) e.Desc.Add(t);
                    c.Offerings.Add(e);
                }
            }
            catch { } // sin ofrendas la app funciona igual

            // ofrenda secreta: el juego muestra el dorso de la carta (no se sabe cual es)
            var secret = new Entry { K = Kind.Offering, Name = "Ofrenda secreta", Killer = "", Rarity = "secreta", Icon = "OfferingsBack.png",
                                     Info = "El killer quemó una ofrenda oculta: no se sabe cuál es hasta que se revela.",
                                     InfoEn = "The Killer burned a hidden offering: you won't know which one until it is revealed.", NameEs = "Ofrenda secreta" };
            foreach (var k in new[] { "ofrenda secreta", "secreta", "secreto", "secret", "secret offering", "oculta", "escondida", "desconocida", "no se cual" })
                secret.AddKey(k);
            c.Offerings.Add(secret);
            // ofrenda de mapa: "el killer nos trajo a este mapa" (cual de todas no importa: el mapa ya se ve)
            var mapOff = new Entry { K = Kind.Offering, Name = "Map offering", Killer = "", Rarity = "mapa", NameEs = "Ofrenda de mapa",
                                     Info = "El killer quemó una ofrenda de mapa: los trajo a este mapa a propósito (lo conoce bien).",
                                     InfoEn = "The Killer burned a map offering: they brought you to this map on purpose (they know it well)." };
            foreach (var k in new[] { "ofrenda de mapa", "ofrenda mapa", "map offering", "mapa del killer" })
                mapOff.AddKey(k);
            mapOff.Synth = true;
            mapOff.Icon = "IconItems_CrypticMap.png"; // el Mapa críptico del juego (si no baja, dice MAPA)
            c.Offerings.Add(mapOff);
            // niebla / ganchos / cofres: en la partida no se nota cual de las 3-4 fue, solo que hay mas niebla, etc.
            Func<string, string, string, string, string, string, string[], Entry> synth = (en, es, tag, info, infoEn, iconOf, keys) =>
            {
                var src = c.Offerings.FirstOrDefault(o => o.Name == iconOf);
                var x = new Entry { K = Kind.Offering, Name = en, NameEs = es, Killer = "", Rarity = tag, Synth = true, Info = info, InfoEn = infoEn, Icon = src != null ? src.Icon : null };
                foreach (var k in keys) x.AddKey(k);
                c.Offerings.Add(x);
                return x;
            };
            synth("Fog offering", "Ofrenda de niebla", "niebla", "El killer quemó una ofrenda de niebla: hay más niebla.", "The Killer burned a fog offering: there is more fog.",
                  "Murky Reagent", new[] { "ofrenda de niebla", "niebla", "mas niebla", "fog offering", "fog", "more fog" });
            synth("Hook offering", "Ofrenda de ganchos", "ganchos", "El killer quemó un oak: los ganchos están más cerca entre sí.", "The Killer burned an oak: hooks are closer together.",
                  "Putrid Oak", new[] { "ofrenda de ganchos", "ganchos cerca", "ganchos mas cerca", "hook offering", "hooks closer", "oak" });
            synth("Chest offering", "Ofrenda de cofres", "cofres", "El killer quemó una moneda: hay menos cofres.", "The Killer burned a coin: there are fewer chests.",
                  "Cut Coin", new[] { "ofrenda de cofres", "menos cofres", "chest offering", "fewer chests", "coin" });

            // mapas: los de hens333.com/callouts (imagenes con el sistema de reloj, hechas por Lethia). La lista se trae
            // de esa pagina solo si no esta guardada o si salio un parche con cambios de mapas; la imagen se baja al elegir el mapa.
            try
            {
                foreach (var line in HensMapList(dir))
                {
                    var f = line.Split('\t');
                    if (f.Length < 3) continue;
                    var names = f[0].Split('|').Where(x => x.Trim().Length > 0).ToList();
                    if (names.Count == 0 || c.MapFiles.ContainsKey(names[0])) continue;
                    var e = new Entry { K = Kind.Offering, Name = names[0], Killer = "", Action = "mapa:" + names[0], ActionTag = f[2] };
                    foreach (var nm in names) { string mi; if (e.Icon == null && Overlay.MapPhoto.TryGetValue(Overlay.Bare(nm), out mi)) e.Icon = "IconMap_" + mi + ".png"; }
                    foreach (var nm in names) { e.AddKey(nm); if (f[2].Length > 0) e.AddKey(f[2] + " " + nm); e.AddKey(Regex.Replace(nm, @"^The\s+", "")); }
                    if (names[0].StartsWith("Police Station")) { e.AddKey("rpd"); e.AddKey("comisaria"); e.AddKey("raccoon city " + names[0]); }
                    if (names[0].StartsWith("Preschool")) foreach (var nm in names) { e.AddKey("badham " + nm); e.AddKey("badham " + Regex.Replace(nm, @"^Preschools*", "")); }
                    c.Maps.Add(e);
                    c.MapFiles[names[0]] = new List<string> { f[1] };
                }
            }
            catch { } // sin mapas la app funciona igual

            c.Info = c.Killers.Count + " killers, " + c.Perks.Count + " perks, " + c.Addons.Count + " addons, " + c.Offerings.Count + " ofrendas, iconos: " +
                     c.Perks.Count(x => x.Icon != null) + " perks / " + c.Addons.Count(x => x.Icon != null) + " addons / " + c.Offerings.Count(x => x.Icon != null) + " ofrendas";
            c.Info += " · datos " + (GameVersion.Length > 0 ? GameVersion : "?") + (remoteVer == null ? " (sin conexión: copia guardada)" : DataNews.Length > 0 ? " (recién actualizados)" : " (al día)");
            SaveVersions(dir);
            return c;
        }
    }

    // ------------------------------------------------------------ leer el nombre del mapa de la pantalla
    // Al empezar la partida el juego muestra abajo a la izquierda el nombre del mapa (y el reino debajo).
    // Con un clic (por defecto el del medio) la app copia SOLO ese pedazo de pantalla y lo lee con el OCR que trae
    // Windows 10/11 (sin IA ni descargas). El compilador de la app (C# 5) no puede referenciar WinRT, asi que se usa por reflexion.
    static class WinOcr
    {
        static Type T(string n, string asm) { return Type.GetType(n + ", " + asm + ", ContentType=WindowsRuntime", false); }
        const string RtAsm = ", System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        static System.Reflection.MethodInfo asTask, asBuffer;
        static object engine; static bool tried;

        static object Await(object op, Type result)
        {
            var task = (System.Threading.Tasks.Task)asTask.MakeGenericMethod(result).Invoke(null, new[] { op });
            task.Wait();
            return task.GetType().GetProperty("Result").GetValue(task);
        }

        // lineas de texto de la imagen; null si este Windows no tiene OCR
        public static string[] Read(Bitmap bmp)
        {
            var tEng = T("Windows.Media.Ocr.OcrEngine", "Windows.Foundation");
            var tSb = T("Windows.Graphics.Imaging.SoftwareBitmap", "Windows.Graphics");
            var tFmt = T("Windows.Graphics.Imaging.BitmapPixelFormat", "Windows.Graphics");
            var tRes = T("Windows.Media.Ocr.OcrResult", "Windows.Foundation");
            if (tEng == null || tSb == null || tFmt == null || tRes == null) return null;
            if (!tried)
            {
                tried = true;
                asTask = Type.GetType("System.WindowsRuntimeSystemExtensions" + RtAsm).GetMethods()
                    .First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                asBuffer = Type.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions" + RtAsm).GetMethods()
                    .First(m => m.Name == "AsBuffer" && m.GetParameters().Length == 1);
                engine = tEng.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
                if (engine == null)
                {
                    // ningun idioma de Windows tiene OCR instalado: probar ingles (los nombres de mapa estan en ingles)
                    var tLang = T("Windows.Globalization.Language", "Windows.Foundation");
                    if (tLang != null) try { engine = tEng.GetMethod("TryCreateFromLanguage").Invoke(null, new[] { Activator.CreateInstance(tLang, "en-US") }); } catch { }
                }
            }
            if (engine == null) return null;
            var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * bmp.Width * 4, bmp.Width * 4);
            bmp.UnlockBits(d);
            var buf = asBuffer.Invoke(null, new object[] { px });
            var sb = tSb.GetMethods().First(m => m.Name == "CreateCopyFromBuffer" && m.GetParameters().Length == 4)
                        .Invoke(null, new object[] { buf, Enum.ToObject(tFmt, 87 /* Bgra8 */), bmp.Width, bmp.Height });
            try
            {
                var res = Await(tEng.GetMethod("RecognizeAsync").Invoke(engine, new[] { sb }), tRes);
                var lines = (System.Collections.IEnumerable)tRes.GetProperty("Lines").GetValue(res);
                return lines.Cast<object>().Select(l => (string)l.GetType().GetProperty("Text").GetValue(l)).ToArray();
            }
            finally { var disp = sb as IDisposable; if (disp != null) disp.Dispose(); }
        }
    }

    static class MapReader
    {
        // zona donde aparece el nombre: abajo a la izquierda (proporcional, sirve para cualquier resolucion)
        public static Rectangle Region(Rectangle game)
        {
            return new Rectangle(game.X, game.Y + (int)(game.Height * 0.72), (int)(game.Width * 0.45), (int)(game.Height * 0.24));
        }

        // agrandada al doble (a la letra fina del juego le sienta mejor) si es chica
        public static Bitmap Prepare(Bitmap crop)
        {
            if (crop.Height >= 400) return new Bitmap(crop);
            var c = new Bitmap(crop.Width * 2, crop.Height * 2);
            using (var g = Graphics.FromImage(c))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(crop, new Rectangle(0, 0, c.Width, c.Height), new Rectangle(0, 0, crop.Width, crop.Height), GraphicsUnit.Pixel);
            }
            return c;
        }

        // avisos de estado (arriba a la derecha): "EXPOSED" y debajo, chico y semitransparente, lo que lo causa ("HEX: NO ONE ESCAPES DEATH")
        public static Rectangle StatusRegion(Rectangle game)
        {
            return new Rectangle(game.X + (int)(game.Width * 0.55), game.Y + (int)(game.Height * 0.08), (int)(game.Width * 0.45), (int)(game.Height * 0.42));
        }

        // x3 en grises con mas contraste: la letra semitransparente sobre fondo claro se lee mejor (el OCR se pasa con las dos versiones)
        public static Bitmap Contrast(Bitmap crop)
        {
            var c = new Bitmap(crop.Width * 3, crop.Height * 3, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(c))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(crop, new Rectangle(0, 0, c.Width, c.Height), new Rectangle(0, 0, crop.Width, crop.Height), GraphicsUnit.Pixel);
            }
            var d = c.LockBits(new Rectangle(0, 0, c.Width, c.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new byte[d.Stride * c.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            for (int i = 0; i < px.Length; i += 4)
            {
                int v = (px[i] * 114 + px[i + 1] * 587 + px[i + 2] * 299) / 1000;
                v = Math.Max(0, Math.Min(255, (v - 120) * 3));
                px[i] = px[i + 1] = px[i + 2] = (byte)v;
            }
            Marshal.Copy(px, 0, d.Scan0, px.Length);
            c.UnlockBits(d);
            return c;
        }

        // perks (y addons) nombrados en el texto leido. El OCR casi nunca lo lee perfecto ("No ONE ESCAPES DEATÀ"), asi que
        // se cuenta cuanto del nombre aparece palabra por palabra (con tolerancia), con y sin "Hex:"/"Scourge Hook:"/"Boon:".
        public static List<Tuple<Entry, double>> MatchSources(Catalog cat, IEnumerable<string> lines, Entry killer, out string why)
        {
            var ls = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var texts = new List<string>(ls);
            for (int i = 0; i + 1 < ls.Count; i++) texts.Add(ls[i] + " " + ls[i + 1]);
            var tw = texts.Select(t => Txt.Norm(t).Split(' ').Where(w => w.Length >= 2).ToArray()).Where(w => w.Length > 0).ToList();
            var cands = cat.Perks.Concat(killer != null ? cat.Addons.Where(a => a.Killer == killer.Name) : cat.Addons);
            var found = new Dictionary<Entry, double>();
            string bestWhy = ""; double bestAny = 0;
            foreach (var e in cands)
            {
                foreach (var name in new[] { e.Name, e.NameEs })
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    string full = Txt.Norm(name);
                    string bare = Regex.Replace(full, @"^(hex|scourge hook|boon|maleficio|gancho de castigo|bendicion)\s+", "");
                    foreach (var key in full == bare ? new[] { full } : new[] { full, bare })
                    {
                        var kw = key.Split(' ').Where(w => w.Length > 0).ToArray();
                        int total = kw.Sum(w => w.Length);
                        foreach (var words in tw)
                        {
                            int cov = 0, n = 0;
                            foreach (var k in kw)
                                if (words.Any(w => k.Length <= 3 ? w == k : Txt.Ratio(k, w) >= 0.75)) { cov += k.Length; n++; }
                            double s = (double)cov / total;
                            bool ok = kw.Length == 1 ? words.Length <= 2 && words.Any(w => Txt.Ratio(kw[0], w) >= 0.85) : n >= 2 && s >= 0.7;
                            if (s > bestAny) { bestAny = s; bestWhy = e.Name + " " + s.ToString("0.00"); }
                            if (!ok) continue;
                            double prev;
                            if (!found.TryGetValue(e, out prev) || s > prev) found[e] = s;
                        }
                    }
                }
            }
            why = found.Count > 0 ? string.Join(", ", found.Select(kv => kv.Key.Name + " " + kv.Value.ToString("0.00"))) : "nada (mejor: " + (bestWhy.Length > 0 ? bestWhy : "-") + ")";
            return found.OrderByDescending(kv => kv.Value).Select(kv => Tuple.Create(kv.Key, kv.Value)).ToList();
        }

        // el OCR a veces lee "II" como "Il", "ll" u "11"
        static string FixRoman(string n)
        {
            var w = n.Split(' ');
            for (int i = 0; i < w.Length; i++)
                if (Regex.IsMatch(w[i], "^[il1]{2,3}$") || (i == w.Length - 1 && i > 0 && Regex.IsMatch(w[i], "^[il1]$")))
                    w[i] = new string('i', w[i].Length);
            return string.Join(" ", w);
        }

        // el mapa cuyo nombre aparece en el texto leido (o null). why: puntaje, para el log
        public static string Match(Catalog cat, string[] lines, out string why)
        {
            why = "";
            if (lines == null || lines.Length == 0) return null;
            var texts = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                texts.Add(lines[i]);
                if (i + 1 < lines.Length) texts.Add(lines[i] + " " + lines[i + 1]); // nombre largo partido en dos renglones
            }
            Entry best = null; double bestS = 0;
            foreach (var t in texts)
            {
                string n = FixRoman(Txt.Norm(t));
                if (n.Length < 3) continue;
                foreach (var e in cat.Maps)
                    foreach (var k in e.Keys)
                    {
                        double s = k.N == n ? 1.01 : Txt.Ratio(n, k.N); // lo exacto gana (Family Residence no es Family Residence II)
                        if (s > bestS) { bestS = s; best = e; }
                    }
            }
            why = best == null ? "nada" : best.Name + " " + Math.Min(bestS, 1).ToString("0.00");
            return best != null && bestS >= 0.85 ? best.Name : null;
        }
    }

    // ------------------------------------------------------------ estado + interpretacion
    class State
    {
        public Entry Killer;
        public List<Entry> Perks = new List<Entry>(), Addons = new List<Entry>();
        static string L(string es, string en) { return Lang.L(es, en); }
        public long LastChange; // hora (unix) del ultimo cambio de la lista: al juntarse un grupo gana la mas nueva
        public bool HasAny() { return Killer != null || Perks.Count > 0 || Addons.Count > 0 || Offering != null; }
        Catalog cat;
        public const double Threshold = 0.66;

        public State(Catalog c) { cat = c; }
        public string ClearKeyName = "Shift+G";

        // sospechas: "creo que tiene X" (se ven semitransparentes con "?"), en orden de cuando se dijeron
        public List<Entry> Doubt = new List<Entry>();
        // addon descripto de forma vaga: candidatos para elegir por numero (el primero queda anotado como sospecha)
        public List<Entry> Pick; public Entry PickEntry; public int PickUntil;
        static readonly Regex PickRx = new Regex(@"^(?:(?:es|era|seria)\s+)?(?:(?:el|la)\s+)?(?:numero\s+|opcion\s+)?(uno|1|primero|primer|primera|dos|2|segundo|segunda|tres|3|tercero|tercer|tercera|cuatro|4|cuarto|cuarta|cinco|5|quinto|quinta|ninguno|ninguna|ningun)\b(?:\s+(?:es|era))?$");
        public bool IsDoubt(Entry e) { return e != null && Doubt.Contains(e); }

        // ofrenda que quemo el killer (una sola)
        public Entry Offering;

        static IEnumerable<Entry> ItemsOf(List<Entry> perks, List<Entry> addons, Entry offering)
        {
            return perks.Concat(addons).Concat(offering != null ? new[] { offering } : new Entry[0]);
        }
        public IEnumerable<Entry> Items() { return ItemsOf(Perks, Addons, Offering); }

        void RemoveEntry(Entry e)
        {
            Perks.Remove(e); Addons.Remove(e);
            if (Offering == e) Offering = null;
            Doubt.Remove(e);
        }

        public void Clear() { Killer = null; Offering = null; Perks.Clear(); Addons.Clear(); Doubt.Clear(); }

        // las sospechas solo valen para lo que sigue anotado
        void Prune() { var all = Items().ToList(); Doubt.RemoveAll(e => !all.Contains(e)); }

        // ---- sala compartida: diferencias entre dos estados, y aplicar las que llegan de otros
        Dictionary<string, object> Ref(Entry e)
        {
            string k = e.K == Kind.Perk ? "perk" : e.K == Kind.Offering ? "ofrenda" : e.Rarity == "nota" ? "nota" : "addon";
            var r = new Dictionary<string, object> { { "k", k }, { "n", e.Name }, { "kl", e.Killer ?? "" } };
            if (IsDoubt(e)) r["d"] = true;
            return r;
        }

        public Dictionary<string, object> Diff(object snap)
        {
            var b = (Snap)snap;
            var before = ItemsOf(b.Perks, b.Addons, b.Offering).ToList();
            var now = Items().ToList();
            var add = new List<object>(); var del = new List<object>(); var conf = new List<object>(); var doubt = new List<object>();
            foreach (var e in before) if (!now.Contains(e)) del.Add(Ref(e));
            foreach (var e in now)
            {
                if (!before.Contains(e)) { add.Add(Ref(e)); continue; }
                if (b.Doubt.Contains(e) && !IsDoubt(e)) conf.Add(Ref(e));
                else if (!b.Doubt.Contains(e) && IsDoubt(e)) doubt.Add(Ref(e));
            }
            var op = new Dictionary<string, object>();
            if (Killer != b.Killer) op["killer"] = Killer != null ? Killer.Name : "";
            if (del.Count > 0) op["del"] = del;
            if (add.Count > 0) op["add"] = add;
            if (conf.Count > 0) op["conf"] = conf;
            if (doubt.Count > 0) op["doubt"] = doubt;
            return op.Count > 0 ? op : null;
        }

        static string RS(Dictionary<string, object> r, string k) { object o; return r.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }

        Entry FindHere(Dictionary<string, object> r)
        {
            string k = RS(r, "k"), n = RS(r, "n");
            if (k == "perk") return Perks.FirstOrDefault(x => x.Name == n);
            if (k == "ofrenda") return Offering != null && Offering.Name == n ? Offering : null;
            if (k == "nota") return Addons.FirstOrDefault(x => x.Rarity == "nota" && x.Name == n);
            return Addons.FirstOrDefault(x => x.Rarity != "nota" && x.Name == n);
        }

        Entry FromRef(Dictionary<string, object> r)
        {
            string k = RS(r, "k"), n = RS(r, "n"), kl = RS(r, "kl");
            if (k == "perk") return cat.Perks.FirstOrDefault(x => x.Name == n);
            if (k == "ofrenda") return cat.Offerings.FirstOrDefault(x => x.Name == n) ?? OfferingByKey(n) ?? OfferingNote(n);
            if (k == "nota") return Note(n);
            return cat.Addons.FirstOrDefault(x => x.Name == n && x.Killer == kl) ?? cat.Addons.FirstOrDefault(x => x.Name == n);
        }

        public string ApplyRemote(Dictionary<string, object> op)
        {
            var done = new List<string>();
            object o;
            if (op.ContainsKey("reset")) { Clear(); done.Add(L("borró la lista", "cleared the list")); }
            if (op.TryGetValue("del", out o) && o is System.Collections.ArrayList)
                foreach (var x in (System.Collections.ArrayList)o)
                {
                    var r = x as Dictionary<string, object>; if (r == null) continue;
                    var e = FindHere(r);
                    if (e != null) { RemoveEntry(e); done.Add("- " + e.Name); }
                }
            if (op.TryGetValue("add", out o) && o is System.Collections.ArrayList)
                foreach (var x in (System.Collections.ArrayList)o)
                {
                    var r = x as Dictionary<string, object>; if (r == null || FindHere(r) != null) continue;
                    var e = FromRef(r); if (e == null) continue;
                    if (e.K == Kind.Perk) { if (Perks.Count >= 4) continue; Perks.Add(e); }
                    else if (e.K == Kind.Offering) { if (Offering != null) Doubt.Remove(Offering); Offering = e; }
                    else { if (Addons.Count >= 2) continue; Addons.Add(e); }
                    bool d = r.ContainsKey("d");
                    if (d) Doubt.Add(e);
                    done.Add("+ " + (e.K == Kind.Offering ? L("ofrenda: ", "offering: ") : "") + e.Name + (d ? " (?)" : ""));
                }
            if (op.TryGetValue("conf", out o) && o is System.Collections.ArrayList)
                foreach (var x in (System.Collections.ArrayList)o)
                {
                    var r = x as Dictionary<string, object>; if (r == null) continue;
                    var e = FindHere(r);
                    if (e != null && Doubt.Remove(e)) done.Add("✓ " + e.Name);
                }
            if (op.TryGetValue("doubt", out o) && o is System.Collections.ArrayList)
                foreach (var x in (System.Collections.ArrayList)o)
                {
                    var r = x as Dictionary<string, object>; if (r == null) continue;
                    var e = FindHere(r);
                    if (e != null && !IsDoubt(e)) { Doubt.Add(e); done.Add(e.Name + " (?)"); }
                }
            if (op.TryGetValue("killer", out o))
            {
                string n = (o ?? "").ToString();
                var k = n.Length == 0 ? null : cat.Killers.FirstOrDefault(x => x.Name == n);
                if (k != Killer) { Killer = k; done.Add("killer: " + (k != null ? k.Name : "-")); }
            }
            Prune();
            return string.Join(", ", done);
        }

        // copia del estado (deshacer, diferencias para la sala)
        public class Snap { public Entry Killer, Offering; public List<Entry> Perks, Addons, Doubt; }
        public object Snapshot() { return new Snap { Killer = Killer, Offering = Offering, Perks = Perks.ToList(), Addons = Addons.ToList(), Doubt = Doubt.ToList() }; }
        public static object EmptySnapshot() { return new Snap { Perks = new List<Entry>(), Addons = new List<Entry>(), Doubt = new List<Entry>() }; }
        public void Restore(object snap)
        {
            var s = (Snap)snap;
            Killer = s.Killer; Offering = s.Offering; Perks = s.Perks.ToList(); Addons = s.Addons.ToList(); Doubt = s.Doubt.ToList();
        }

        class Hit { public Entry E; public double S; }

        // ---- apodos del usuario (apodos.txt): "apodo = nombre real"
        public string AliasPath;
        DateTime aliasStamp = DateTime.MinValue;
        readonly Dictionary<string, List<Key>> aliasKeys = new Dictionary<string, List<Key>>();
        string lastFail; Entry lastGuess; // para aprender cuando dice "si"
        public void ForgetGuess() { lastFail = null; lastGuess = null; } // lo que dicen otros no deja preguntas pendientes

        const string AliasHeader =
            "# Apodos: una linea por apodo ->  apodo = nombre real\r\n" +
            "# Tambien se agregan por voz: \"apodo pain res es Pain Resonance\",\r\n" +
            "# o diciendo \"si\" cuando el overlay pregunta (¿...?).\r\n" +
            "# Los cambios en este archivo se aplican solos (no hace falta reiniciar).\r\n";

        public int AliasCount { get { return aliasKeys.Values.Sum(l => l.Count); } }

        IEnumerable<Entry> AllEntries() { return cat.Killers.Concat(cat.Perks).Concat(cat.Addons).Concat(cat.Offerings); }

        Entry Resolve(string name)
        {
            string n = Txt.Norm(name);
            var e = AllEntries().FirstOrDefault(x => Txt.Norm(x.Name) == n);
            if (e != null) return e;
            var h = Best(n, AllEntries(), false);
            return h != null && h.S >= 0.8 ? h.E : null;
        }

        public void ReloadAliases()
        {
            if (AliasPath == null) return;
            try
            {
                if (!File.Exists(AliasPath)) File.WriteAllText(AliasPath, AliasHeader, new UTF8Encoding(true));
                var stamp = File.GetLastWriteTimeUtc(AliasPath);
                if (stamp == aliasStamp) return;
                aliasStamp = stamp;
                aliasKeys.Clear();
                foreach (var line in File.ReadAllLines(AliasPath, Encoding.UTF8))
                {
                    var l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int i = l.IndexOf('=');
                    if (i <= 0) continue;
                    string nick = Txt.Norm(l.Substring(0, i));
                    var e = Resolve(l.Substring(i + 1).Trim());
                    if (nick.Length == 0 || e == null) continue;
                    List<Key> list;
                    if (!aliasKeys.TryGetValue(e.Name, out list)) aliasKeys[e.Name] = list = new List<Key>();
                    list.Add(new Key(nick));
                }
            }
            catch { }
        }

        void AddAlias(string nick, Entry e)
        {
            nick = Txt.Norm(nick);
            if (nick.Length == 0 || AliasPath == null) return;
            try
            {
                ReloadAliases();
                var lines = File.ReadAllLines(AliasPath, Encoding.UTF8).ToList();
                lines.RemoveAll(x =>
                {
                    int i = x.IndexOf('=');
                    return !x.TrimStart().StartsWith("#") && i > 0 && Txt.Norm(x.Substring(0, i)) == nick;
                });
                lines.Add(nick + " = " + e.Name);
                File.WriteAllLines(AliasPath, lines, new UTF8Encoding(true));
            }
            catch { }
            aliasStamp = DateTime.MinValue;
            ReloadAliases();
        }

        static double Score(string pn, Key pk, Key k)
        {
            if (pn == k.N) return 1.0;
            double a = Txt.Ratio(pk.P, k.P);
            double b = (pk.S.Length >= 2 && k.S.Length >= 2) ? Txt.Ratio(pk.S, k.S) : a;
            double s = 0.6 * a + 0.4 * b;
            // "pain res" -> "pain resonance", "corrupt" -> "corrupt intervention"
            if (pk.W.Length <= k.W.Length && pn.Replace(" ", "").Length >= 4)
            {
                bool ok = true;
                for (int i = 0; i < pk.W.Length; i++) if (!k.W[i].StartsWith(pk.W[i])) { ok = false; break; }
                if (ok) s = Math.Max(s, 0.86 + 0.1 * pn.Length / k.N.Length);
            }
            return s;
        }

        // ---- traza de depuracion (la escribe el overlay en logs\debug.log)
        public List<string> Trace = new List<string>();
        void T(string s) { Trace.Add(s); }
        static string F2(double d) { return d.ToString("0.00", CultureInfo.InvariantCulture); }

        public string Describe()
        {
            return "killer=" + (Killer != null ? Killer.Name : "-") +
                   " | perks: " + (Perks.Count > 0 ? string.Join(", ", Perks.Select(p => p.Name + (IsDoubt(p) ? " (?)" : ""))) : "-") +
                   " | addons: " + (Addons.Count > 0 ? string.Join(", ", Addons.Select(a => a.Name + (a.Rarity == "nota" ? " (nota)" : "") + (IsDoubt(a) ? " (?)" : ""))) : "-") +
                   (Offering != null ? " | ofrenda: " + Offering.Name + (Offering.Rarity == "nota" ? " (nota)" : "") + (IsDoubt(Offering) ? " (?)" : "") : "");
        }

        Hit Best(string piece, IEnumerable<Entry> pool, bool log = true)
        {
            var pk = new Key(piece);
            var per = new List<Hit>();
            int skipped = 0;
            foreach (var e in pool)
            {
                if (exclude.Contains(e)) { skipped++; continue; } // lo que se rechazo con "no, borra eso"
                List<Key> al;
                var keys = aliasKeys.TryGetValue(e.Name, out al) ? e.Keys.Concat(al) : e.Keys;
                double bs = -1;
                foreach (var k in keys) bs = Math.Max(bs, Score(piece, pk, k));
                per.Add(new Hit { E = e, S = bs });
            }
            per.Sort((a, b) => b.S.CompareTo(a.S));
            if (log)
                T("  buscar \"" + piece + "\" entre " + per.Count + (skipped > 0 ? " (excluidos " + skipped + ")" : "") + ": " +
                  string.Join(", ", per.Take(3).Select(h => h.E.Name + " " + F2(h.S))));
            return per.Count > 0 ? per[0] : null;
        }

        bool doubtMode; // la frase actual empezo con "creo que" / "probablemente"...

        // elegido de la lista (cuadro de texto): sin interpretar nada, con deshacer como siempre
        public string AddPicked(Entry e, bool doubt)
        {
            var before = (Snap)Snapshot();
            // un addon sin killer anotado: el killer sale del addon
            if (e.K == Kind.Addon && Killer == null && !string.IsNullOrEmpty(e.Killer))
                Killer = cat.Killers.FirstOrDefault(k => k.Name == e.Killer);
            doubtMode = doubt;
            string r = AddOne(e);
            doubtMode = false;
            Prune();
            PushUndo(before, new List<Entry> { e }, null);
            return r;
        }

        // lo anotado ahora (para "quitar", "cambiar" y "duda" desde el cuadro de escribir)
        public List<Entry> Current()
        {
            var l = new List<Entry>(Perks);
            l.AddRange(Addons);
            if (Offering != null) l.Add(Offering);
            return l;
        }

        public bool IsFull(Entry e)
        {
            return e.K == Kind.Perk ? Perks.Count >= 4 && !Perks.Contains(e) : e.K == Kind.Addon ? Addons.Count >= 2 && !Addons.Contains(e) : false;
        }

        public string RemovePicked(Entry e)
        {
            var before = (Snap)Snapshot();
            bool ok = Perks.Remove(e) | Addons.Remove(e);
            if (Offering == e) { Offering = null; ok = true; }
            Doubt.Remove(e);
            if (!ok) return L("no estaba: ", "nothing to remove: ") + e.Name;
            PushUndo(before, null, null);
            return "- " + e.Name;
        }

        // cambiar uno por otro en el mismo lugar (el nuevo hereda si era sospecha, salvo que se diga lo contrario)
        public string ReplacePicked(Entry old, Entry neu, bool doubt)
        {
            var before = (Snap)Snapshot();
            var list = old.K == Kind.Perk ? Perks : old.K == Kind.Addon ? Addons : null;
            if (old.K == Kind.Offering) Offering = neu;
            else if (list == null || !list.Contains(old)) return L("no estaba: ", "nothing to replace: ") + old.Name;
            else if (list.Contains(neu)) list.Remove(old);
            else list[list.IndexOf(old)] = neu;
            Doubt.Remove(old);
            if (doubt) Doubt.Add(neu); else Doubt.Remove(neu);
            PushUndo(before, new List<Entry> { neu }, null);
            return old.Name + " → " + neu.Name + (doubt ? " (?)" : "");
        }

        // sospecha <-> seguro
        public string ToggleDoubt(Entry e)
        {
            var before = (Snap)Snapshot();
            if (!Current().Contains(e)) return L("no estaba: ", "nothing to remove: ") + e.Name;
            PushUndo(before, null, null);
            if (Doubt.Remove(e)) return "✓ " + e.Name + L(" confirmado", " confirmed");
            Doubt.Add(e);
            return e.Name + L(" ahora es sospecha (?)", " is now a guess (?)");
        }

        // cambio de killer: pide confirmacion si ya habia uno; al cambiar se van los addons (y sus sospechas) del anterior
        public Entry PendingKiller; public int PendingUntil;
        public bool ConfirmedKiller; // el cuadro de escribir ya pregunto
        public string ChangeKiller(Entry k)
        {
            PendingKiller = null;
            bool had = Killer != null && Killer != k;
            int n = Addons.Count;
            Killer = k;
            if (had && n > 0) { foreach (var a in Addons) Doubt.Remove(a); Addons.Clear(); }
            return "killer: " + k.Name + (had && n > 0 ? L(" · addons borrados", " · add-ons cleared") : "");
        }

        string AddOne(Entry e)
        {
            if (e.K == Kind.Killer)
            {
                if (Killer == e) return L("el killer ya es ", "the killer is already ") + e.Name;
                // ya habia otro killer: se pide confirmacion (cambiar borra los addons del anterior)
                if (Killer != null && !ConfirmedKiller)
                {
                    PendingKiller = e; PendingUntil = Environment.TickCount + 20000;
                    return L("¿cambiar ", "change ") + Killer.Name + L(" por ", " to ") + e.Name + L("? se borran los addons · decí/escribí \"sí\"", "? this clears the add-ons · say/type \"yes\"");
                }
                return ChangeKiller(e);
            }
            if (e.K == Kind.Offering)
            {
                if (Offering == e || (Offering != null && Offering.Name == e.Name))
                {
                    if (!doubtMode && Doubt.Remove(Offering)) return L("✓ ofrenda ", "✓ offering ") + Offering.Name + L(" confirmada", " confirmed");
                    return L("la ofrenda ", "the offering ") + e.Name + L(" ya estaba", " was already there");
                }
                string prev = Offering != null ? L(" (antes ", " (was ") + Offering.Name + ")" : "";
                if (Offering != null) Doubt.Remove(Offering);
                Offering = e;
                if (doubtMode) { Doubt.Add(e); return L("+ ofrenda: ", "+ offering: ") + e.Name + " (?)" + prev; }
                return L("+ ofrenda: ", "+ offering: ") + e.Name + prev;
            }
            var list = e.K == Kind.Perk ? Perks : Addons;
            int max = e.K == Kind.Perk ? 4 : 2;
            if (list.Contains(e))
            {
                // "tiene X" con X en duda = lo confirma
                if (!doubtMode && Doubt.Remove(e)) return "✓ " + e.Name + L(" confirmado", " confirmed");
                return e.Name + L(" ya estaba", " was already there");
            }
            if (list.Count >= max) return L("ya tiene ", "already has ") + max + (max == 4 ? " perks" : L(" addons", " add-ons")) + L(", no anoté ", ", didn't add ") + e.Name + L(" (decí \"quitar ...\")", " (say \"remove ...\")");
            list.Add(e);
            if (doubtMode) { Doubt.Add(e); return "+ " + e.Name + " (?)"; }
            return "+ " + e.Name;
        }

        // "creo que tiene X", "probablemente tiene X", "capaz que tiene X"...
        static readonly Regex DoubtRx = new Regex(@"^(?:(?:yo\s+)?creo\s+que|probablemente|capaz(?:\s+que)?|quizas?|quiza|tal\s+vez|puede\s+(?:ser\s+)?que|me\s+parece\s+que|sospecho\s+que|posiblemente|por\s+ahi)\s+");
        // "confirmado", "confirmado X", "sí tiene X"
        static readonly Regex ConfirmRx = new Regex(@"^(?:confirm(?:ado|ada|o|ar|alo|adisimo)|si\s+(?:lo\s+)?tiene|efectivamente(?:\s+tiene)?|tenia\s+razon|era\s+nomas)\b\s*(.*)$");
        // "descartado", "descartado X", "al final no", "no lo tiene"
        static readonly Regex DenyRx = new Regex(@"^(?:descart(?:ado|ada|o|ar|alo)|al\s+final\s+no(?:\s+(?:lo\s+)?(?:tiene|tenia|era))?|no\s+lo\s+(?:tiene|tenia)|no\s+era\s+nomas)\b\s*(.*)$");

        Entry PickDoubt(string what)
        {
            if (Doubt.Count == 0) return null;
            what = Strip(what);
            if (what.Length == 0) return Doubt[Doubt.Count - 1]; // la ultima sospecha
            var h = Best(what, Doubt);
            return h != null && h.S >= Threshold ? h.E : null;
        }

        // devuelve el resultado, o null para seguir con el camino normal (t puede quedar reescrita)
        string ConfirmOrDeny(ref string t)
        {
            var cm = ConfirmRx.Match(t);
            if (cm.Success)
            {
                string rest = cm.Groups[1].Value;
                T("modo: confirmar -> \"" + rest + "\" (sospechas: " + Doubt.Count + ")");
                var e = PickDoubt(rest);
                if (e != null) { Doubt.Remove(e); return "✓ " + e.Name + L(" confirmado", " confirmed"); }
                if (Strip(rest).Length == 0) return L("no hay nada en duda para confirmar", "nothing to confirm");
                t = "tiene " + rest; // "confirmado X" con X que no estaba en duda: se anota seguro
                return null;
            }
            var dm = DenyRx.Match(t);
            if (dm.Success)
            {
                string rest = dm.Groups[1].Value;
                T("modo: descartar -> \"" + rest + "\" (sospechas: " + Doubt.Count + ")");
                var e = PickDoubt(rest);
                if (e != null) { RemoveEntry(e); return "✗ " + e.Name + L(" descartado", " discarded"); }
                if (Strip(rest).Length == 0) return L("no hay nada en duda para descartar", "nothing to discard");
                t = "quitar " + rest; // "descartado X" con X seguro: se quita igual
                return null;
            }
            return null;
        }

        static readonly Regex Fillers = new Regex(@"^(tiene|tienes|tenga|tendria|tenia|tienen|lleva|lleve|llevaba|usa|use|usaba|tambien|ademas|creo que|el|la|los|las|un|una|con|perk|perks|addon|addons|add on|add ons|accesorio|y|e|o)\s+");

        static string Strip(string t)
        {
            string prev;
            do { prev = t; t = Fillers.Replace(t, ""); } while (t != prev);
            t = Regex.Replace(t, @"\s+(tambien|creo)$", "");
            return t.Trim();
        }

        // el killer se guarda solo para filtrar addons; no se muestra en pantalla
        IEnumerable<Entry> AddPool(bool killerOnly)
        {
            if (killerOnly) return cat.Killers;
            return cat.Perks.Concat(KillerAddons()).Concat(cat.Offerings);
        }

        List<Entry> KillerAddons()
        {
            return Killer == null ? cat.Addons : cat.Addons.Where(a => a.Killer == Killer.Name).ToList();
        }

        // compara lo que el usuario dice que hace el addon con las descripciones en espanol
        Entry MatchDesc(string text, List<Entry> pool, out List<Entry> ties)
        {
            ties = new List<Entry>();
            var u = Txt.Tokens(text).Distinct().ToList();
            T("  descripcion -> palabras: " + string.Join(" ", u));
            if (u.Count == 0 || pool.Count == 0) return null;
            int D = pool.Count;
            var w = new Dictionary<string, double>();
            foreach (var x in u) { int df = pool.Count(e => e.Desc.Contains(x)); w[x] = df == 0 ? 1.0 : Math.Log((D + 1.0) / (df + 0.5)); }
            // M = peso de las palabras que coinciden; las que no coinciden restan como mucho 1 cada una
            // (las descripciones vagas dejan palabras sin coincidir: restan la mitad)
            var scored = pool.Select(e =>
            {
                double M = u.Where(x => e.Desc.Contains(x)).Sum(x => w[x]);
                double miss = u.Where(x => !e.Desc.Contains(x)).Sum(x => Math.Min(w[x], 1.0));
                return new { E = e, M = M, S = M + miss > 0 ? M / (M + 0.5 * miss) : 0 };
            }).OrderByDescending(x => x.M * x.S).ThenByDescending(x => x.M).ToList();
            T("  descripcion -> mejores: " + string.Join(", ", scored.Take(4).Select(x => x.E.Name + " " + F2(x.S) + "/" + F2(x.M))));
            var best = scored[0];
            if (best.M <= 0) return null;
            var second = scored.Count > 1 ? scored[1] : null;
            // seguro: coincide bien y le saca ventaja al segundo
            if (best.S >= 0.6 && best.M >= 0.7 && (second == null || second.M * second.S < best.M * best.S * 0.7)) return best.E;
            // si no: los 3 mejores para elegir mirando los iconos
            ties = scored.Where(x => x.M > 0 && x.M * x.S >= best.M * best.S * 0.35).Take(3).Select(x => x.E).ToList();
            return null;
        }

        // ---- "no, borra eso": deshace el ultimo cambio y no repite la misma eleccion
        object undoSnap; List<Entry> undoAdded = new List<Entry>(); string undoAlias;
        // pasos anteriores para deshacer de a uno (hasta 20; cada uno es una copia chica de lo anotado)
        readonly List<Tuple<object, List<Entry>, string>> undoHist = new List<Tuple<object, List<Entry>, string>>();
        void PushUndo(object before, List<Entry> added, string alias)
        {
            if (undoSnap != null) { undoHist.Add(Tuple.Create(undoSnap, undoAdded, undoAlias)); if (undoHist.Count > 20) undoHist.RemoveAt(0); }
            undoSnap = before; undoAdded = added; undoAlias = alias;
        }
        void PopUndo()
        {
            if (undoHist.Count == 0) { undoSnap = null; undoAdded = new List<Entry>(); undoAlias = null; return; }
            var t = undoHist[undoHist.Count - 1];
            undoHist.RemoveAt(undoHist.Count - 1);
            undoSnap = t.Item1; undoAdded = t.Item2; undoAlias = t.Item3;
        }
        public bool CanUndo { get { return undoSnap != null; } }
        HashSet<Entry> exclude = new HashSet<Entry>();
        string learnedAlias; // apodo aprendido en el ultimo Apply (para olvidarlo si se deshace)

        static readonly Regex UndoRx = new Regex(
            @"^(no\s+)*(borra(r|lo)?|borrame|saca(r|lo)?|quita(r|lo)?|deshace(r|lo)?|deshaz)(\s+eso|\s+lo ultimo|\s+el ultimo)?(\s+no)?$|^(no\s+)*(eso no|no era eso|eso esta mal|me equivoque|esta mal|mal)$");

        void RemoveAlias(string nick)
        {
            if (nick == null || AliasPath == null) return;
            try
            {
                var lines = File.ReadAllLines(AliasPath, Encoding.UTF8).ToList();
                lines.RemoveAll(x => { int i = x.IndexOf('='); return !x.TrimStart().StartsWith("#") && i > 0 && Txt.Norm(x.Substring(0, i)) == nick; });
                File.WriteAllLines(AliasPath, lines, new UTF8Encoding(true));
            }
            catch { }
            aliasStamp = DateTime.MinValue;
            ReloadAliases();
        }

        // frases en ingles -> la misma orden en español (el resto del analisis es uno solo)
        static readonly Tuple<Regex, string>[] EnglishCmds = new[] {
            Tuple.Create(new Regex(@"^\W*(?:i think|i guess|maybe|probably|might have|could be)\s+(?:(?:he|she|it|they)\s+)?(?:(?:has|have|had|got|is running|runs|running)\s+)?(?:(?:the|a|an)\s+)?", RegexOptions.IgnoreCase), "creo que tiene "),
            Tuple.Create(new Regex(@"^\W*(?:(?:the\s+)?killer\s+is|it'?s|its|it\s+is)\s+(?:the\s+)?(?=\w)", RegexOptions.IgnoreCase), "el killer es "),
            Tuple.Create(new Regex(@"^\W*(?:(?:he|she|it|they)\s+)?(?:has|have|had|got|is running|runs|running|brought)\s+(?:(?:the|a|an)\s+)?", RegexOptions.IgnoreCase), "tiene "),
            Tuple.Create(new Regex(@"^\W*(?:remove|delete|take off|take out|drop)\s+(?:the\s+)?", RegexOptions.IgnoreCase), "quitar "),
            Tuple.Create(new Regex(@"^\W*(?:undo|scratch that|that'?s wrong|wrong one|not that)\W*$", RegexOptions.IgnoreCase), "borra eso"),
            Tuple.Create(new Regex(@"^\W*(?:yes|yeah|yep|yup|correct|right|that'?s it|that one)\W*$", RegexOptions.IgnoreCase), "si"),
            Tuple.Create(new Regex(@"^\W*(?:nope|nah|cancel)\W*$", RegexOptions.IgnoreCase), "no"),
            Tuple.Create(new Regex(@"^\W*none(?:\s+of\s+(?:those|them|these))?\W*$", RegexOptions.IgnoreCase), "ninguno"),
            Tuple.Create(new Regex(@"^\W*(?:confirm|confirmed)\b", RegexOptions.IgnoreCase), "confirmado"),
            Tuple.Create(new Regex(@"^\W*(?:discard|discarded)\b", RegexOptions.IgnoreCase), "descartado"),
            Tuple.Create(new Regex(@"^\W*(?:offering|burned|burnt)\s+(?:(?:a|an|the)\s+)?", RegexOptions.IgnoreCase), "ofrenda "),
            Tuple.Create(new Regex(@"^\W*(?:nickname|alias)\s+(.+?)\s+(?:is|means|=)\s+", RegexOptions.IgnoreCase), "apodo $1 es "),
            Tuple.Create(new Regex(@"\badd[\s-]?ons?\s+(?:that|which)\s+", RegexOptions.IgnoreCase), "addon que "),
        };
        static readonly Regex EnglishPick = new Regex(@"^\W*(?:the\s+)?(?:number\s+)?(one|first|two|second|three|third|four|fourth|five|fifth)(?:\s+one)?\W*$", RegexOptions.IgnoreCase);
        public static string FromEnglish(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var pm = EnglishPick.Match(raw);
            if (pm.Success)
            {
                string w = pm.Groups[1].Value.ToLowerInvariant();
                return "el " + (w == "one" || w == "first" ? "uno" : w == "two" || w == "second" ? "dos" : w == "three" || w == "third" ? "tres" : w == "four" || w == "fourth" ? "cuatro" : "cinco");
            }
            foreach (var c in EnglishCmds)
                if (c.Item1.IsMatch(raw)) { raw = c.Item1.Replace(raw, c.Item2, 1); if (!c.Item2.EndsWith(" ")) break; }
            return raw;
        }

        public string Apply(string raw)
        {
            raw = FromEnglish(raw);
            string t0 = Txt.Norm(raw);
            // "no" a secas: descarta la sugerencia (¿...?) o, si no habia, deshace lo ultimo
            bool bareNo = Regex.IsMatch(t0, @"^(no|no no|nop|nah|negativo)$");
            if (bareNo && PendingKiller != null)
            {
                PendingKiller = null;
                return L("ok, sigue ", "ok, still ") + (Killer != null ? Killer.Name : L("sin killer", "no killer"));
            }
            if (bareNo && lastGuess != null)
            {
                lastGuess = null; lastFail = null;
                return L("ok, no lo anoto", "ok, not adding it");
            }
            if (bareNo || UndoRx.IsMatch(t0))
            {
                if (undoSnap == null) return L("no hay nada para deshacer", "nothing to undo");
                Restore(undoSnap);
                if (undoAlias != null) RemoveAlias(undoAlias);
                if (undoAdded == null) undoAdded = new List<Entry>();
                exclude = new HashSet<Entry>(undoAdded.Where(x => x.Rarity != "nota"));
                string what = undoAdded.Count > 0 ? string.Join(", ", undoAdded.Select(x => x.Name)) : L("el último cambio", "the last change");
                PopUndo();
                return L("deshice: ", "undone: ") + what + (exclude.Count > 0 ? L(" · repetilo, no lo voy a elegir de nuevo", " · say it again, I won't pick that one") : "");
            }

            var before = (Snap)Snapshot();
            learnedAlias = null;
            doubtMode = false;
            string res = ApplyInner(raw);
            doubtMode = false;
            Prune();
            exclude.Clear(); // la exclusion vale solo para el intento siguiente

            var added = new List<Entry>();
            if (Killer != null && Killer != before.Killer) added.Add(Killer);
            added.AddRange(Perks.Where(p => !before.Perks.Contains(p)));
            added.AddRange(Addons.Where(a => !before.Addons.Contains(a)));
            if (Offering != null && Offering != before.Offering) added.Add(Offering);
            bool changed = Killer != before.Killer || !Perks.SequenceEqual(before.Perks) || !Addons.SequenceEqual(before.Addons)
                        || !Doubt.SequenceEqual(before.Doubt) || Offering != before.Offering;
            if (changed || learnedAlias != null)
            {
                PushUndo(before, added, learnedAlias);
            }
            return res;
        }

        // devuelve la descripcion de lo que hizo
        // Whisper escribe "addon" como "a Don", "lado", "un don"... (sobre todo antes de un color)
        const string ColorWords = @"(?:rojo|roja|rojos|violeta|violetas|morado|morada|p[uú]rpura|lila|verde|verdes|amarill[oa]|marr[oó]n|caf[eé]|iridiscente)";
        static string FixAddonWord(string raw)
        {
            raw = Regex.Replace(raw, @"\b(?:a\s+don|adon|ad\s+on|a\s+dom|el\s+don|un\s+don|la\s+don|lado|a\s+dones|adones)\b(?=[\s,]+(?:(?:ese|que|de|del)\s+)?" + ColorWords + @"\b|[\s,]+(?:ese|que)\b)",
                                "addon", RegexOptions.IgnoreCase);
            // "tiene (el/uno) violeta" = "tiene addon violeta"
            raw = Regex.Replace(raw, @"^(\W*(?:tiene|tenia|tenía|lleva|trajo)\s+)(?:(?:el|un|uno|una)\s+)?(?=" + ColorWords + @"\b)", "$1addon ", RegexOptions.IgnoreCase);
            return raw;
        }

        string ApplyInner(string raw)
        {
            raw = FixAddonWord(raw);
            string t = Txt.Norm(raw);
            if (t.Length == 0) return L("no escuché nada", "didn't hear anything");
            // borrar todo NO se hace por voz (Whisper podria entenderlo sin querer): se usa la tecla
            if (Regex.IsMatch(t, @"\b(nueva partida|otra partida|borra(r)? todo|limpia(r)?( todo)?|reinicia(r)?|reset)\b"))
                return L("no borro por voz: mantené ", "no clearing by voice: hold ") + ClearKeyName + L(" 1 segundo", " for 1 second");
            ReloadAliases();

            // cambio de killer pendiente: "sí" lo confirma (y borra los addons), cualquier otra cosa lo cancela
            if (PendingKiller != null)
            {
                var pk = PendingKiller; PendingKiller = null;
                if (Environment.TickCount - PendingUntil < 0 && Regex.IsMatch(t, @"^(?:si|si si|dale|confirmo|confirmar|cambia(?:r|lo)?|ok|okey|si cambia(?:r|lo)?)$"))
                    return ChangeKiller(pk);
                if (Regex.IsMatch(t, @"^(?:no|nah|cancela(?:r)?|deja(?:lo)?)$")) return "sigue " + (Killer != null ? Killer.Name : "sin killer");
            }

            // muletillas al principio: "ah ...", "anotá (que) ...", "poneme ..."
            t = Regex.Replace(t, @"^(?:(?:ah|eh|bueno|ok|anota(?:me|le)?|anata|anote|pone(?:me|le)?|agrega(?:me|le)?)\s+(?:(?:de|que)\s+)?)+", "");
            // como escribe Whisper "el killer es": "Killers: Killers ...", "killeresk", "alquileres", "el que eres"
            // (tambien "kirer es", "kiler es"; "killer" solo no se toca: "killer X" ya se entiende)
            t = Regex.Replace(t, @"^(?:(?:el|al)\s+)?(?:al|el)?(?:k|qu)i(?:ll?|r)er(?:e?s|esk|\s+es)\b(?:\s+(?:k|qu)i(?:ll?|r)er(?:e?s|esk|\s+es)?\b)*", "el killer es");
            t = Regex.Replace(t, @"^el que eres?\b", "el killer es");
            if (t.Length == 0) return L("no entendí", "didn't get that");

            // elegir entre los addons candidatos: "el dos", "el primero", "ninguno"
            if (Pick != null)
            {
                var pm = PickRx.Match(t);
                if (Environment.TickCount - PickUntil > 0 || !Addons.Contains(PickEntry)) Pick = null;
                else if (pm.Success)
                {
                    string w = pm.Groups[1].Value;
                    var cands = Pick; var old = PickEntry;
                    Pick = null;
                    if (w.StartsWith("ningun")) { RemoveEntry(old); return "- addon " + old.Name + L(" (ninguno de esos: describilo de otra forma)", " (none of those: describe it another way)"); }
                    int idx = w == "uno" || w == "1" || w.StartsWith("primer") ? 0 : w == "dos" || w == "2" || w.StartsWith("segund") ? 1
                            : w == "cuatro" || w == "4" || w.StartsWith("cuart") ? 3 : w == "cinco" || w == "5" || w.StartsWith("quint") ? 4 : 2;
                    if (idx >= cands.Count) return L("solo había ", "there were only ") + cands.Count + L(" opciones", " options");
                    var ch = cands[idx];
                    Addons[Addons.IndexOf(old)] = ch;
                    Doubt.Remove(old);
                    return "addon: " + ch.Name + " ✓";
                }
            }

            // "si" despues de "no entendí (¿X?)": anota X y aprende como lo dijo
            var guess = lastGuess; var fail = lastFail;
            lastGuess = null; lastFail = null;
            if (guess != null && Regex.IsMatch(t, @"^(si|si si|si eso|eso|eso es|exacto|correcto|dale|si era eso|si es eso|ese)$"))
            {
                AddAlias(fail, guess);
                learnedAlias = Txt.Norm(fail);
                return AddOne(guess) + L(" · aprendí \"", " · learned \"") + fail + "\"";
            }

            // "apodo pain res es Pain Resonance"
            // (acepta cosas antes: "anota el apodo ...", "guarda el alias ...")
            var tm = Regex.Match(t, @"(?:^|\s)(?:apodo|alias|aprende que|aprende|aprendete que|aprendete|cuando digo|cuando yo digo)\s+(.+?)\s+(?:es|son|de|para|significa|quiere decir|me refiero a|seria|igual a)\s+(.+)$");
            if (tm.Success)
            {
                string nick = Strip(tm.Groups[1].Value), target = Strip(tm.Groups[2].Value);
                // "es el killer Cenobite" / "es el perk Grim Embrace": solo se busca en ese tipo
                IEnumerable<Entry> tpool = AllEntries();
                var kind = Regex.Match(target, @"^(?:(?:el|la|un|una)\s+)?(killer|asesino|perk|addon|add on|accesorio|ofrenda)\s+");
                if (kind.Success)
                {
                    string kw = kind.Groups[1].Value;
                    tpool = kw == "killer" || kw == "asesino" ? (IEnumerable<Entry>)cat.Killers
                          : kw == "perk" ? cat.Perks : kw == "ofrenda" ? cat.Offerings : (IEnumerable<Entry>)cat.Addons;
                    target = Strip(target.Substring(kind.Length));
                }
                var h = Best(target, tpool);
                if (nick.Length == 0 || h == null || h.S < Threshold) return L("no entendí a qué te referís con \"", "didn't get what you mean by \"") + target + "\"";
                AddAlias(nick, h.E);
                learnedAlias = Txt.Norm(nick);
                return L("aprendí: \"", "learned: \"") + nick + "\" = " + h.E.Name;
            }

            // "confirmado (X)" / "descartado (X)": resuelve sospechas
            string cd = ConfirmOrDeny(ref t);
            if (cd != null) return cd;

            // "creo que tiene X": se anota como sospecha
            var dmx = DoubtRx.Match(t);
            if (dmx.Success) { doubtMode = true; t = t.Substring(dmx.Length); T("sospecha: \"" + dmx.Value.Trim() + "\""); }

            // "No, el killer es Pinhead" / "no, es la Nurse": el "no" es una correccion, no un "quitar"
            var corr =Regex.Match(t, @"^(?:no\s+)+(?=(?:(?:el|la)\s+)?(?:killer|asesino|perk)\b|es\b|era\b)");
            if (corr.Success) t = t.Substring(corr.Length);
            t = Regex.Replace(t, @"^era\s+", "es ");

            bool remove = false;
            var m = Regex.Match(t, @"^(no tiene|no lleva|quita(r|le)?|saca(r|le)?|borra(r|le)?|elimina(r)?)\s+");
            if (m.Success) { remove = true; t = t.Substring(m.Length); }
            bool killerOnly = false;
            // "el killer es X", "el killer X", "killer X", "es la X", "contra la X"...
            var km = Regex.Match(t, @"^(?:(?:(?:el|la)\s+)?(?:killer|asesino)(?:\s+es)?(?:\s+(?:el|la))?|es\s+(?:el|la)\s+(?:killer|asesino)(?:\s+(?:el|la))?|es\s+(?:el|la)|es|(?:contra|juega con)(?:\s+(?:el|la))?|el que juega es(?:\s+(?:el|la))?)\s+");
            if (km.Success && !remove) { killerOnly = true; t = t.Substring(km.Length); }
            // "tiene el perk Monstrous Shrine": solo perks
            bool perkOnly = !remove && !killerOnly && Regex.IsMatch(t, @"\bperks?\b");
            T("normalizado: \"" + Txt.Norm(raw) + "\"" + (corr.Success ? " (con \"no\" de correccion)" : ""));
            T("modo: " + (remove ? "quitar" : killerOnly ? "killer" : perkOnly ? "solo perks" : "agregar") + " -> \"" + t + "\"" +
              (Killer != null && !killerOnly && !remove ? " (addons filtrados por " + Killer.Name + ")" : ""));

            if (remove && Regex.IsMatch(t, @"^((los|el|un|una) )?(add ?ons?|adons?|accesorios?)$"))
            {
                if (Addons.Count == 0) return L("no hay addons para quitar", "there are no add-ons to remove");
                Addons.Clear();
                return L("addons borrados", "add-ons cleared");
            }

            // ofrenda: "ofrenda Ebony Mori", "trajo un mori", "quemó Shroud of Separation", "quitar ofrenda"
            var om = OfferingRx.Match(t);
            if (!om.Success && !remove) om = BurnRx.Match(t);
            if (om.Success)
            {
                string rest = om.Groups[1].Value.Trim();
                T("ruta ofrenda: \"" + rest + "\"");
                if (remove)
                {
                    if (Offering == null) return L("no hay ofrenda para quitar", "there's no offering to remove");
                    string n = Offering.Name; RemoveEntry(Offering);
                    return L("- ofrenda ", "- offering ") + n;
                }
                return AddOfferingText(rest);
            }

            // "addon hachas mas rapidas": el usuario describe el addon con sus palabras
            if (!remove)
            {
                var am = AddonRx.Match(doubtMode ? DoubtRawRx.Replace(raw.Trim(), "") : raw.Trim());
                if (am.Success)
                {
                    T("ruta addon: \"" + am.Groups[1].Value + "\"");
                    return AddAddonText(am.Groups[1].Value);
                }
            }

            t = Strip(t);
            if (t.Length == 0) return L("no entendí", "didn't get that");

            var pool = remove ? Items().ToList() : perkOnly ? cat.Perks.ToList() : AddPool(killerOnly).ToList();
            if (pool.Count == 0) return L("no hay nada para quitar", "nothing to remove");

            // una sola cosa vs varias separadas por "y"
            var whole = Best(t, pool);
            var pieces = Regex.Split(t, @"\s+(?:y|e|and|mas|tambien|con|coma|tiene)\s+").Select(Strip).Where(p => p.Length > 0).ToList();
            List<Hit> hits = null;
            if (pieces.Count > 1)
            {
                var ph = pieces.Select(p => Best(p, pool)).ToList();
                double minS = ph.Min(h => h == null ? 0 : h.S);
                if (minS >= Threshold && (whole == null || minS >= whole.S - 0.05)) hits = ph;
                T("partes: " + string.Join(" | ", pieces) + " -> " + (hits != null ? "usa las partes" : "usa la frase entera") +
                  " (minimo partes " + F2(minS) + ", entera " + (whole != null ? F2(whole.S) : "-") + ", umbral " + F2(Threshold) + ")");
            }
            if (hits == null)
            {
                if (whole == null || whole.S < Threshold)
                {
                    // ¿era un killer dicho raro? ("unkaneki", "el gull"): se prueba cada palabra contra los killers
                    if (!remove && !perkOnly)
                    {
                        Hit kb = null;
                        foreach (var w in t.Split(' ').Where(x => x.Length >= 4).Concat(new[] { t }))
                        {
                            var kh = Best(w, cat.Killers, false);
                            if (kh != null && (kb == null || kh.S > kb.S)) kb = kh;
                        }
                        if (kb != null) T("  killer por palabra: " + kb.E.Name + " " + F2(kb.S));
                        if (kb != null && kb.S >= 0.75 && (whole == null || kb.S > whole.S)) return AddOne(kb.E);
                    }
                    if (whole == null || whole.S < 0.4) return L("no entendí", "didn't get that");
                    // no ofrecer "¿X?" (ni aprender con "sí") si la frase era un comando que no se entendió
                    if (!remove && !Regex.IsMatch(t, @"\b(apodo|alias|killers?|asesino|anota|ofrenda)\b")) { lastGuess = whole.E; lastFail = t; }
                    else return L("no entendí", "didn't get that");
                    return L("no entendí (¿", "didn't get that (") + whole.E.Name + "?)" + (remove ? "" : L(" — decí \"sí\" si era eso", " — say \"yes\" if that's it"));
                }
                hits = new List<Hit> { whole };
            }

            var done = new List<string>();
            foreach (var h in hits.GroupBy(x => x.E).Select(g => g.First())) // "tiene X tiene X" = una sola vez
            {
                var e = h.E;
                if (remove)
                {
                    RemoveEntry(e);
                    done.Add("- " + e.Name);
                }
                else done.Add(AddOne(e));
            }
            return string.Join(", ", done);
        }

        // el mismo "creo que..." pero sobre el texto original (con tildes y mayusculas) para la ruta de addons
        static readonly Regex DoubtRawRx = new Regex(@"^\W*(?:(?:yo\s+)?creo\s+que|probablemente|capaz(?:\s+que)?|quiz[aá]s?|tal\s+vez|puede\s+(?:ser\s+)?que|me\s+parece\s+que|sospecho\s+que|posiblemente|por\s+ah[ií])[\s,.]+", RegexOptions.IgnoreCase);

        static readonly Regex AddonRx = new Regex(
            @"^\W*(?:(?:tienes?|tenga|lleva|lleve|usa|use|tambien|y)\s+)*(?:(?:el|un|una|los|unos|de)\s+)?(?:a[dh]{1,2}[\s-]?[oó]ns?|accesorios?)\b[\s,.:;-]*(?:(?:que\s+)?hace\s+que\s+|(?:que|de|del|para)\s+)?(.+?)[\s.!?¡¿,]*$",
            RegexOptions.IgnoreCase);

        // "(tiene/trajo) (la) ofrenda (es/de) X"
        static readonly Regex OfferingRx = new Regex(@"^(?:(?:tiene|tenia|trajo|trae|quemo|quema|uso|usa|puso|lleva|llevaba)\s+)?(?:(?:la|una|un|su)\s+)?(?:ofrendas?|offerings?)\b\s*(?:(?:es|era|fue|que)\s+)?(?:(?:la|el|un|una)\s+)?(.*)$");
        // "trajo un mori", "quemó Shroud of Separation" (sin decir "ofrenda")
        static readonly Regex BurnRx = new Regex(@"^(?:trajo|trae|quemo|quema|puso)\s+(?:(?:la|el|una|un|su)\s+)?(.+)$");

        // ofrenda por nombre exacto o por uno de sus apodos ("secreta" -> Ofrenda secreta)
        Entry OfferingByKey(string text)
        {
            string n = Txt.Norm(text);
            return cat.Offerings.FirstOrDefault(o => Txt.Norm(o.Name) == n || o.Keys.Any(k => k.N == n));
        }

        static Entry OfferingNote(string text)
        {
            text = text.Trim();
            if (text.Length > 0) text = char.ToUpper(text[0]) + text.Substring(1);
            var e = new Entry { K = Kind.Offering, Name = text, Killer = "", Rarity = "nota" };
            e.AddKey(text);
            return e;
        }

        string AddOfferingText(string text)
        {
            string n = Strip(Regex.Replace(text, @"^(de|del)\s+", ""));
            if (n.Length == 0) return L("¿qué ofrenda? (decí \"ofrenda\" y el nombre o lo que hace)", "no offering name (say \"offering\" and its name or what it does)");
            // 1) el nombre (ingles o espanol, o apodo como "mori")
            var best = Best(n, cat.Offerings);
            if (best != null && best.S >= 0.75) return AddOne(best.E);
            // 2) por lo que hace / el mapa ("de mapa de Haddonfield", "que pone mas niebla")
            List<Entry> ties;
            var e = MatchDesc(text, cat.Offerings, out ties);
            if (e != null) return AddOne(e);
            string extra = ties.Count > 0 ? L(" (puede ser ", " (could be ") + string.Join(L(" o ", " or "), ties.Take(3).Select(x => x.Name)) + ")" : L(" (no la reconocí)", " (didn't recognize it)");
            // 3) queda anotada tal cual la dijo
            return AddOne(OfferingNote(text)) + extra;
        }

        static Entry Note(string text)
        {
            text = text.Trim();
            if (text.Length > 0) text = char.ToUpper(text[0]) + text.Substring(1);
            var e = new Entry { K = Kind.Addon, Name = text, Killer = "", Rarity = "nota" };
            e.AddKey(text);
            return e;
        }

        // color de la rareza como lo dice la gente: rojo = iridiscente, violeta, verde, amarillo, marron
        internal static readonly Regex ColorRx = new Regex(@"\b(rojos?|rojas?|red|iridiscentes?|iri|visceral|violetas?|morad[oa]s?|purpuras?|lilas?|purple|violet|verdes?|green|amarill[oa]s?|yellow|marron(?:es)?|cafe|brown)\b");
        internal static string ColorRarity(string w)
        {
            if (Regex.IsMatch(w, "^(roj|red|irid|iri$|visc)")) return "visceral";
            if (Regex.IsMatch(w, "^(viol|morad|purp|lila)")) return "veryrare";
            if (Regex.IsMatch(w, "^(verd|green)")) return "rare";
            if (Regex.IsMatch(w, "^(amari|yell)")) return "uncommon";
            return "common";
        }
        static string ColorName(string rar)
        {
            if (Lang.En) switch (rar) { case "visceral": return "red"; case "veryrare": return "purple"; case "rare": return "green"; case "uncommon": return "yellow"; default: return "brown"; }
            switch (rar) { case "visceral": return "rojo"; case "veryrare": return "violeta"; case "rare": return "verde"; case "uncommon": return "amarillo"; default: return "marrón"; }
        }

        static string ColorPlural(string rar) { return Lang.En ? ColorName(rar) : rar == "common" ? "marrones" : ColorName(rar) + "s"; }

        // varios candidatos: se anota el primero como sospecha y se elige con "el dos"
        string OfferPick(List<Entry> cands, string why)
        {
            var e = cands[0];
            Pick = cands; PickEntry = e; PickUntil = Environment.TickCount + 30000;
            Addons.Add(e);
            if (!Doubt.Contains(e)) Doubt.Add(e);
            return "+ addon: " + e.Name + " (?) · " + (why.Length > 0 ? why + " " : "") + L("¿cuál es? ", "which one? ") + string.Join(" · ", cands.Select((x, i) => (i + 1) + " " + x.Name)) + L(" (decí \"el dos\"…)", " (say \"number two\"…)");
        }

        string AddAddonText(string text)
        {
            text = Regex.Replace(text, @"^\W*(?:(?:tienes?|lleva|usa|tambien|también)\b[\s.,:;…]*)+", "", RegexOptions.IgnoreCase).Trim();
            // "el addon ese rojo (que hace X)": el color filtra por rareza
            string rar = null;
            var colm = ColorRx.Match(Txt.Norm(text));
            if (colm.Success)
            {
                rar = ColorRarity(colm.Groups[1].Value);
                text = Txt.Norm(text).Remove(colm.Index, colm.Length).Trim();
                T("color: " + colm.Groups[1].Value + " -> " + rar);
            }
            if (rar != null && Killer != null)
            {
                if (Addons.Count >= 2) return L("ya tiene 2 addons (decí \"quitar ...\" o \"quitar addons\")", "already has 2 add-ons (say \"remove ...\" or \"remove add-ons\")");
                var byColor = KillerAddons().Where(a => a.Rarity == rar && !Addons.Contains(a)).ToList();
                if (byColor.Count == 0) return Lang.En ? "there are no " + ColorName(rar) + " add-ons for " + Killer.Name : "no hay addons " + ColorPlural(rar) + " de " + Killer.Name;
                Entry ce = null;
                if (Txt.Tokens(text).Count > 0)
                {
                    var bn = Best(Strip(Txt.Norm(text)), byColor);
                    if (bn != null && bn.S >= 0.78) ce = bn.E;
                    List<Entry> cties = new List<Entry>();
                    if (ce == null) ce = MatchDesc(text, byColor, out cties);
                    if (ce == null && cties.Count > 1) return OfferPick(cties, ColorName(rar) + ":");
                    if (ce == null && cties.Count == 1) ce = cties[0];
                }
                if (ce == null && byColor.Count == 1) ce = byColor[0];
                if (ce == null) return OfferPick(byColor.Take(5).ToList(), L("hay ", "") + byColor.Count + " " + ColorPlural(rar) + ":");
                Addons.Add(ce);
                if (doubtMode) Doubt.Add(ce);
                return "+ addon: " + ce.Name + (doubtMode ? " (?)" : "");
            }
            string n = Strip(Txt.Norm(text));
            if (n.Length == 0) return L("no entendí qué hace el addon", "didn't get what the add-on does");
            var pool = KillerAddons();
            // 1) dijo el nombre real (en ingles o en espanol)
            var best = Best(n, pool);
            Entry e = (best != null && best.S >= (Killer != null ? 0.78 : 0.85)) ? best.E : null;
            // 1b) en realidad es un perk ("Monstrous Shrine" transcripto como addon)
            if (e == null)
            {
                var bp = Best(n, cat.Perks);
                if (bp != null && bp.S >= 0.8 && (best == null || bp.S > best.S)) return AddOne(bp.E) + L(" (era un perk)", " (it was a perk)");
            }
            if (Addons.Count >= 2) return L("ya tiene 2 addons (decí \"quitar ...\" o \"quitar addons\")", "already has 2 add-ons (say \"remove ...\" or \"remove add-ons\")");
            string extra = "";
            // 2) con el killer anotado: buscar por lo que hace
            if (e == null && Killer != null)
            {
                List<Entry> ties;
                e = MatchDesc(text, pool, out ties);
                ties = ties.Where(x => !Addons.Contains(x)).ToList();
                if (e == null && ties.Count == 1) { e = ties[0]; doubtMode = true; }
                else if (e == null && ties.Count > 1) return OfferPick(ties, ""); // dudoso: elegir con "el dos"
                else if (e == null) extra = L(" (no lo reconocí entre los addons de ", " (not one of the add-ons of ") + Killer.Name + ")";
            }
            else if (e == null) extra = L(" (decí el killer para reconocerlo)", " (say the killer so I can recognize it)");
            // 3) si no, queda la descripcion tal cual
            if (e == null) e = Note(text);
            if (Addons.Any(a => a.Name == e.Name)) return e.Name + L(" ya estaba", " was already there");
            Addons.Add(e);
            if (doubtMode) { Doubt.Add(e); extra = " (?)" + extra; }
            return "+ addon: " + e.Name + extra;
        }

        public string Prompt()
        {
            // ojo: nada de "Addon:" al principio; Whisper lo copiaba y todo terminaba como addon.
            // Los nombres de killers y los apodos ayudan a que Whisper los escriba bien ("Pinhead", no "Been Hit").
            var names = cat.Killers.Select(k => Regex.Replace(k.Name, "^The ", "")).ToList();
            names.Add("Pinhead"); names.Add("Myers"); names.Add("Freddy"); names.Add("Chucky"); names.Add("Wesker"); names.Add("Jason"); names.Add("Memento Mori");
            var nicks = aliasKeys.Values.SelectMany(l => l).Select(k => k.N).Distinct().Take(25);
            return L("Dead by Daylight. Tiene Pain Resonance, Grim Embrace y Deadlock. ", "Dead by Daylight. He has Pain Resonance, Grim Embrace and Deadlock. ") + string.Join(", ", names) +
                   (nicks.Any() ? ". " + string.Join(", ", nicks) : "") + ".";
        }

        public void Save(string path)
        {
            var l = new List<string>();
            if (Killer != null) l.Add("killer=" + Killer.Name);
            foreach (var p in Perks) l.Add("perk=" + p.Name);
            foreach (var a in Addons) l.Add(a.Rarity == "nota" ? "nota=" + a.Name : "addon=" + a.Name + "|" + a.Killer);
            if (Offering != null) l.Add((Offering.Rarity == "nota" ? "ofrendanota=" : "ofrenda=") + Offering.Name);
            foreach (var d in Doubt) l.Add("duda=" + d.Name);
            if (LastChange > 0) l.Add("cambio=" + LastChange);
            try { File.WriteAllLines(path, l, Encoding.UTF8); } catch { }
        }

        public void LoadFrom(string path)
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line.Substring(0, i), v = line.Substring(i + 1);
                if (k == "killer") Killer = cat.Killers.FirstOrDefault(x => x.Name == v);
                else if (k == "ofrenda") Offering = cat.Offerings.FirstOrDefault(x => x.Name == v);
                else if (k == "ofrendanota") Offering = OfferingByKey(v) ?? OfferingNote(v); // notas viejas ("Secreta") pasan a la ofrenda real
                else if (k == "perk") { var e = cat.Perks.FirstOrDefault(x => x.Name == v); if (e != null) Perks.Add(e); }
                else if (k == "nota") Addons.Add(Note(v));
                else if (k == "addon")
                {
                    var parts = v.Split('|');
                    var e = cat.Addons.FirstOrDefault(x => x.Name == parts[0] && (parts.Length < 2 || x.Killer == parts[1]));
                    if (e != null) Addons.Add(e);
                }
                else if (k == "cambio") { long lc; if (long.TryParse(v, out lc)) LastChange = lc; }
                else if (k == "duda")
                {
                    var e = Items().FirstOrDefault(x => x.Name == v);
                    if (e != null && !Doubt.Contains(e)) Doubt.Add(e);
                }
            }
        }
    }

    // ------------------------------------------------------------ procesos hijos (mueren con el programa)
    static class Job
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);

        [StructLayout(LayoutKind.Sequential)]
        struct BASIC { public long a, b; public uint LimitFlags; public UIntPtr c, d; public uint e; public UIntPtr f; public uint g, h; }
        [StructLayout(LayoutKind.Sequential)]
        struct IOC { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct EXT { public BASIC Basic; public IOC Io; public UIntPtr a, b, c, d; }

        static IntPtr handle = IntPtr.Zero;

        public static void Add(Process p)
        {
            try
            {
                if (handle == IntPtr.Zero)
                {
                    handle = CreateJobObject(IntPtr.Zero, null);
                    var info = new EXT();
                    info.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
                    int len = Marshal.SizeOf(typeof(EXT));
                    IntPtr ptr = Marshal.AllocHGlobal(len);
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(handle, 9, ptr, (uint)len);
                    Marshal.FreeHGlobal(ptr);
                }
                AssignProcessToJobObject(handle, p.Handle);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------ microfono (waveIn de Windows -> PCM 16 kHz mono)
    // Sin ffmpeg: usa el microfono predeterminado de Windows o uno elegido por nombre en config.ini.
    class Mic
    {
        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")] static extern int waveInGetDevCaps(IntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, IntPtr dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEINCAPS { public short wMid, wPid; public int vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public int dwFormats; public short wChannels, wReserved1; }

        const int WAVE_MAPPER = -1, CALLBACK_EVENT = 0x50000, WAVE_MAPPED = 0x4, WHDR_DONE = 1;
        const int NBUF = 8, BUFBYTES = 3200; // 8 buffers de 100 ms

        readonly short[] buf = new short[16000 * 60];
        long total;
        readonly object lk = new object();
        public volatile string LastError = "";
        public volatile bool Running;
        public string DeviceName = "";
        volatile bool stop;
        IntPtr h = IntPtr.Zero;
        IntPtr[] hdrs = new IntPtr[NBUF], datas = new IntPtr[NBUF];
        AutoResetEvent ev;
        static readonly int HDRSIZE = Marshal.SizeOf(typeof(WAVEHDR));
        static readonly int OFF_RECORDED = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
        static readonly int OFF_FLAGS = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");

        public static List<string> Devices()
        {
            var l = new List<string>();
            int n = waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WAVEINCAPS();
                if (waveInGetDevCaps(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0) l.Add(caps.szPname);
            }
            return l;
        }

        // device: vacio = predeterminado de Windows; si no, el primero cuyo nombre empiece igual (Windows corta a 31 letras)
        public void Start(string device)
        {
            int id = WAVE_MAPPER;
            DeviceName = Lang.L("predeterminado de Windows", "Windows default");
            if (!string.IsNullOrEmpty(device))
            {
                var devs = Devices();
                for (int i = 0; i < devs.Count; i++)
                {
                    string a = devs[i].Trim(), b = device.Trim();
                    if (a.Length > 0 && (b.StartsWith(a, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                    { id = i; DeviceName = devs[i]; break; }
                }
                if (id == WAVE_MAPPER) LastError = Lang.L("no encontré \"", "couldn't find \"") + device + Lang.L("\", uso el predeterminado", "\", using the default");
            }
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = 16000, nAvgBytesPerSec = 32000, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0 };
            ev = new AutoResetEvent(false);
            int r = waveInOpen(out h, new IntPtr(id), ref fmt, ev.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT | (id == WAVE_MAPPER ? 0 : WAVE_MAPPED));
            if (r != 0) throw new Exception("waveInOpen error " + r);
            for (int i = 0; i < NBUF; i++)
            {
                datas[i] = Marshal.AllocHGlobal(BUFBYTES);
                hdrs[i] = Marshal.AllocHGlobal(HDRSIZE);
                var hd = new WAVEHDR { lpData = datas[i], dwBufferLength = BUFBYTES };
                Marshal.StructureToPtr(hd, hdrs[i], false);
                waveInPrepareHeader(h, hdrs[i], HDRSIZE);
                waveInAddBuffer(h, hdrs[i], HDRSIZE);
            }
            r = waveInStart(h);
            if (r != 0) throw new Exception("waveInStart error " + r);
            Running = true; stop = false;
            new Thread(Loop) { IsBackground = true }.Start();
        }

        void Loop()
        {
            var tmp = new byte[BUFBYTES];
            int next = 0;
            try
            {
                while (!stop)
                {
                    ev.WaitOne(500);
                    // los buffers vuelven en orden
                    for (int guard = 0; guard < NBUF && !stop; guard++)
                    {
                        IntPtr hp = hdrs[next];
                        if ((Marshal.ReadInt32(hp, OFF_FLAGS) & WHDR_DONE) == 0) break;
                        int n = Marshal.ReadInt32(hp, OFF_RECORDED);
                        if (n > 0)
                        {
                            Marshal.Copy(datas[next], tmp, 0, n);
                            lock (lk) for (int i = 0; i + 1 < n; i += 2) Push((short)(tmp[i] | (tmp[i + 1] << 8)));
                        }
                        Marshal.WriteInt32(hp, OFF_FLAGS, Marshal.ReadInt32(hp, OFF_FLAGS) & ~WHDR_DONE);
                        if (!stop) waveInAddBuffer(h, hp, HDRSIZE);
                        next = (next + 1) % NBUF;
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            Running = false;
            var s = Stopped;
            if (!stop && s != null) s();
        }
        public Action Stopped;

        void Push(short s) { buf[total % buf.Length] = s; total++; }

        public long Pos { get { lock (lk) return total; } }

        public short[] Get(long from, long to)
        {
            lock (lk)
            {
                if (from < total - buf.Length + 1) from = total - buf.Length + 1;
                if (from < 0) from = 0;
                if (to > total) to = total;
                if (to <= from) return new short[0];
                var r = new short[to - from];
                for (long i = from; i < to; i++) r[i - from] = buf[i % buf.Length];
                return r;
            }
        }

        public void Stop()
        {
            stop = true;
            try
            {
                if (h != IntPtr.Zero)
                {
                    waveInReset(h);
                    for (int i = 0; i < NBUF; i++) if (hdrs[i] != IntPtr.Zero) waveInUnprepareHeader(h, hdrs[i], HDRSIZE);
                    waveInClose(h);
                    h = IntPtr.Zero;
                }
            }
            catch { }
        }
    }

    // ------------------------------------------------------------ sala compartida (ntfy.sh)
    // Cada cambio se manda como "diferencia" (agregar/quitar/killer). Todos aplican los mensajes
    // en el orden del servidor (incluso los propios), asi terminan con la misma lista.
    class Room
    {
        public string Topic;
        public readonly string Me = Guid.NewGuid().ToString("N").Substring(0, 8);
        public Action<Dictionary<string, object>> OnOp;
        public Action<string> OnStatus;
        volatile bool stop;
        HttpWebRequest cur;
        string lastId;
        readonly System.Collections.Concurrent.BlockingCollection<string> outbox = new System.Collections.Concurrent.BlockingCollection<string>();

        public void Start(string topic)
        {
            Topic = topic; stop = false; lastId = null;
            new Thread(Listen) { IsBackground = true }.Start();
            new Thread(Send) { IsBackground = true }.Start();
        }

        public void Stop()
        {
            stop = true;
            try { if (cur != null) cur.Abort(); } catch { }
            outbox.Add(null); // despierta al que envia
        }

        void Listen()
        {
            var js = new JavaScriptSerializer();
            int backoff = 1000;
            string topic = Topic;
            while (!stop)
            {
                try
                {
                    // al entrar trae lo de las ultimas 12 h (lo que guarda ntfy) y despues sigue escuchando
                    var req = (HttpWebRequest)WebRequest.Create("https://ntfy.sh/" + topic + "/json?since=" + (lastId ?? "12h"));
                    req.UserAgent = "DbdOverlay/1.0"; req.Timeout = 20000; req.ReadWriteTimeout = 120000;
                    cur = req;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        if (OnStatus != null) OnStatus(Lang.L("conectado", "connected"));
                        backoff = 1000;
                        string line;
                        while (!stop && (line = sr.ReadLine()) != null)
                        {
                            if (line.Trim().Length == 0) continue;
                            var d = js.Deserialize<Dictionary<string, object>>(line);
                            object ev;
                            if (!d.TryGetValue("event", out ev) || (ev ?? "").ToString() != "message") continue;
                            lastId = d["id"].ToString();
                            try
                            {
                                var op = js.Deserialize<Dictionary<string, object>>(d["message"].ToString());
                                if (OnOp != null) OnOp(op);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (stop) break;
                    if (OnStatus != null) OnStatus(Lang.L("sin conexión (", "offline (") + ex.Message + Lang.L("), reintento…", "), retrying…"));
                    Thread.Sleep(backoff);
                    backoff = Math.Min(backoff * 2, 30000);
                }
            }
        }

        public static void PublishNow(string topic, Dictionary<string, object> op) { PublishNow(topic, op, 0); }
        // waitMs > 0: espera a que salga (como mucho ese tiempo), para usar al cerrar la app
        public static void PublishNow(string topic, Dictionary<string, object> op, int waitMs)
        {
            string body = new JavaScriptSerializer().Serialize(op);
            var th = new Thread(() =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://ntfy.sh/" + topic);
                    req.Method = "POST"; req.UserAgent = "DbdOverlay/1.0"; req.Timeout = 15000;
                    req.ContentType = "text/plain; charset=utf-8";
                    var b = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = b.Length;
                    using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                    using (req.GetResponse()) { }
                }
                catch { }
            }) { IsBackground = true };
            th.Start();
            if (waitMs > 0) th.Join(waitMs);
        }

        public void Publish(Dictionary<string, object> op)
        {
            op["from"] = Me;
            outbox.Add(new JavaScriptSerializer().Serialize(op));
        }

        void Send()
        {
            string topic = Topic;
            while (!stop)
            {
                string body = outbox.Take();
                if (body == null || stop) break;
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        var req = (HttpWebRequest)WebRequest.Create("https://ntfy.sh/" + topic);
                        req.Method = "POST"; req.UserAgent = "DbdOverlay/1.0"; req.Timeout = 15000;
                        req.ContentType = "text/plain; charset=utf-8";
                        var b = Encoding.UTF8.GetBytes(body);
                        req.ContentLength = b.Length;
                        using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                        using (req.GetResponse()) { }
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (OnStatus != null) OnStatus(Lang.L("no pude enviar (", "couldn't send (") + ex.Message + Lang.L("), reintento…", "), retrying…"));
                        Thread.Sleep(1500 * (i + 1));
                    }
                }
            }
        }

        // codigo de sala: 10 letras/numeros faciles de dictar (sin 0/O/1/I/L)
        public static string NewCode()
        {
            const string abc = "abcdefghjkmnpqrstuvwxyz23456789";
            var rnd = new byte[10];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(rnd);
            return new string(rnd.Select(x => abc[x % abc.Length]).ToArray());
        }

        public static string CleanCode(string s)
        {
            s = Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9]", "");
            if (s.StartsWith("dbdov")) s = s.Substring(5);
            if (s.StartsWith("sala") && s.Length >= 14) s = s.Substring(4);
            if (s.StartsWith("room") && s.Length >= 14) s = s.Substring(4); // "sala abcd2345ef" (como se pega en el chat)
            return s;
        }

        public static string TopicFor(string code) { return "dbdov-" + code; }
    }

    // ------------------------------------------------------------ Steam (steam_api64.dll que trae DBD; nada de memoria del juego)
    static class SteamDbd
    {
        public const uint AppId = 381210;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string p);
        [DllImport("steam_api64.dll")] static extern int SteamAPI_InitFlat(byte[] err); // 0 = OK (SDK 1.58+)
        [DllImport("steam_api64.dll")] static extern void SteamAPI_Shutdown();
        [DllImport("steam_api64.dll")] static extern void SteamAPI_RunCallbacks();
        [DllImport("steam_api64.dll")] static extern IntPtr SteamAPI_SteamFriends_v017();
        [DllImport("steam_api64.dll")] static extern IntPtr SteamAPI_SteamUser_v023();
        [DllImport("steam_api64.dll")] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr u);
        [DllImport("steam_api64.dll")] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr f, int flags);
        [DllImport("steam_api64.dll")] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr f, int i, int flags);
        [DllImport("steam_api64.dll")] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr f, ulong id);
        [DllImport("steam_api64.dll")] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr f, ulong id, byte[] info);
        [DllImport("steam_api64.dll")] static extern void SteamAPI_ISteamFriends_RequestFriendRichPresence(IntPtr f, ulong id);
        [DllImport("steam_api64.dll")] static extern int SteamAPI_ISteamFriends_GetFriendRichPresenceKeyCount(IntPtr f, ulong id);
        [DllImport("steam_api64.dll")] static extern IntPtr SteamAPI_ISteamFriends_GetFriendRichPresenceKeyByIndex(IntPtr f, ulong id, int i);
        [DllImport("steam_api64.dll")] static extern IntPtr SteamAPI_ISteamFriends_GetFriendRichPresence(IntPtr f, ulong id, string key);

        static string U8(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n);
            return Encoding.UTF8.GetString(b);
        }

        public static string FindDll()
        {
            var libs = new List<string>();
            string steam = null;
            try { steam = (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) ?? "").ToString().Replace('/', '\\'); } catch { }
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            libs.Add(steam);
            try
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(steam, @"steamapps\libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch { }
            foreach (var l in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string bin = Path.Combine(l, @"steamapps\common\Dead by Daylight\Engine\Binaries\ThirdParty\Steamworks");
                if (!Directory.Exists(bin)) continue;
                var f = Directory.GetFiles(bin, "steam_api64.dll", SearchOption.AllDirectories).OrderByDescending(x => x).FirstOrDefault();
                if (f != null) return f;
            }
            return null;
        }

        public static bool GameRunning() { return Process.GetProcessesByName("DeadByDaylight-Win64-Shipping").Length > 0 || Process.GetProcessesByName("DeadByDaylight-EGS-Shipping").Length > 0; }

        static string Init()
        {
            string dll = FindDll();
            if (dll == null) return Lang.L("no encontré steam_api64.dll de Dead by Daylight", "couldn't find Dead by Daylight's steam_api64.dll");
            SetDllDirectory(Path.GetDirectoryName(dll));
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
            var err = new byte[1024];
            int ir = SteamAPI_InitFlat(err);
            return ir == 0 ? null : Lang.L("Steam no respondió (", "Steam didn't respond (") + ir + ": " + Encoding.UTF8.GetString(err).TrimEnd('\0') + ")";
        }

        // proceso vigia (hijo del overlay): mientras DBD este abierto, avisa por stdout mi lobby y que amigos estan en el mismo
        // {"lobby":"...","me":"<steamid>","party":{"<steamid>":"nombre"}} solo cuando cambia
        public static void Watch(int parentPid)
        {
            var js = new JavaScriptSerializer();
            string e = Init();
            if (e != null) { Console.Out.WriteLine(js.Serialize(new Dictionary<string, object> { { "error", e } })); Console.Out.Flush(); return; }
            try
            {
                IntPtr fr = SteamAPI_SteamFriends_v017(), us = SteamAPI_SteamUser_v023();
                ulong me = SteamAPI_ISteamUser_GetSteamID(us);
                var info = new byte[24];
                string last = null;
                while (true)
                {
                    try { if (Process.GetProcessById(parentPid).HasExited) break; } catch { break; }
                    if (!GameRunning()) break;
                    SteamAPI_RunCallbacks();
                    ulong lobby = 0;
                    Array.Clear(info, 0, info.Length);
                    if (SteamAPI_ISteamFriends_GetFriendGamePlayed(fr, me, info) && (BitConverter.ToUInt64(info, 0) & 0xFFFFFF) == AppId)
                        lobby = BitConverter.ToUInt64(info, 16);
                    var party = new Dictionary<string, object>();
                    if (lobby != 0)
                    {
                        int n = SteamAPI_ISteamFriends_GetFriendCount(fr, 4);
                        for (int i = 0; i < n; i++)
                        {
                            ulong id = SteamAPI_ISteamFriends_GetFriendByIndex(fr, i, 4);
                            Array.Clear(info, 0, info.Length);
                            if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(fr, id, info)) continue;
                            if ((BitConverter.ToUInt64(info, 0) & 0xFFFFFF) != AppId || BitConverter.ToUInt64(info, 16) != lobby) continue;
                            party[id.ToString()] = U8(SteamAPI_ISteamFriends_GetFriendPersonaName(fr, id));
                        }
                    }
                    string line = js.Serialize(new Dictionary<string, object> { { "lobby", lobby == 0 ? "" : lobby.ToString() }, { "me", me.ToString() }, { "party", party } });
                    if (line != last) { Console.Out.WriteLine(line); Console.Out.Flush(); last = line; }
                    Thread.Sleep(2000);
                }
            }
            finally { SteamAPI_Shutdown(); }
        }

        // prueba: quien de mis amigos esta en DBD, con que lobby, y que rich presence publica el juego
        public static string Probe()
        {
            var sb = new StringBuilder();
            sb.Append("dll: " + (FindDll() ?? "NO ENCONTRADA") + "\r\njuego abierto: " + GameRunning() + "\r\n");
            string ie = Init();
            if (ie != null) { sb.Append(ie + "\r\n"); return sb.ToString(); }
            try
            {
                IntPtr fr = SteamAPI_SteamFriends_v017(), us = SteamAPI_SteamUser_v023();
                ulong me = SteamAPI_ISteamUser_GetSteamID(us);
                var ids = new List<ulong> { me };
                int n = SteamAPI_ISteamFriends_GetFriendCount(fr, 4); // k_EFriendFlagImmediate
                for (int i = 0; i < n; i++) ids.Add(SteamAPI_ISteamFriends_GetFriendByIndex(fr, i, 4));
                sb.Append("amigos: " + n + "\r\n");
                var inDbd = new List<ulong>();
                var info = new byte[24];
                foreach (var id in ids)
                {
                    Array.Clear(info, 0, info.Length);
                    if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(fr, id, info)) continue;
                    if ((BitConverter.ToUInt64(info, 0) & 0xFFFFFF) != AppId) continue;
                    inDbd.Add(id);
                    SteamAPI_ISteamFriends_RequestFriendRichPresence(fr, id);
                }
                for (int i = 0; i < 30; i++) { SteamAPI_RunCallbacks(); Thread.Sleep(100); }
                foreach (var id in inDbd)
                {
                    Array.Clear(info, 0, info.Length);
                    SteamAPI_ISteamFriends_GetFriendGamePlayed(fr, id, info);
                    sb.Append((id == me ? "YO " : "AMIGO ") + "#" + (id % 100000) + " " + U8(SteamAPI_ISteamFriends_GetFriendPersonaName(fr, id)) +
                              " · ip " + BitConverter.ToUInt32(info, 8) + ":" + BitConverter.ToUInt16(info, 12) + " · lobby " + BitConverter.ToUInt64(info, 16) + "\r\n");
                    int k = SteamAPI_ISteamFriends_GetFriendRichPresenceKeyCount(fr, id);
                    for (int i = 0; i < k; i++)
                    {
                        string key = U8(SteamAPI_ISteamFriends_GetFriendRichPresenceKeyByIndex(fr, id, i));
                        sb.Append("      " + key + " = " + U8(SteamAPI_ISteamFriends_GetFriendRichPresence(fr, id, key)) + "\r\n");
                    }
                }
                if (inDbd.Count == 0) sb.Append("nadie en DBD (¿ni yo?)\r\n");
            }
            finally { SteamAPI_Shutdown(); }
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------ lo que suena en la PC (WASAPI loopback): Discord + juego
    class Loopback
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            int Initialize(int shareMode, int flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
            int GetBufferSize(out int frames);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out int padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long def, out long min);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr h);
            int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out int frames, out int flags, out long devPos, out long qpcPos);
            int ReleaseBuffer(int frames);
            int GetNextPacketSize(out int frames);
        }

        public string LastError = "";
        public int Rate, Channels, Bits;
        volatile bool stop;
        // salida a 16 kHz mono (igual que el microfono)
        readonly short[] ring = new short[16000 * 120];
        long written;
        readonly object lk = new object();

        public long Pos { get { lock (lk) return written; } }
        public short[] Get(long from, long to)
        {
            lock (lk)
            {
                from = Math.Max(from, written - ring.Length); to = Math.Min(to, written);
                if (to <= from) return new short[0];
                var r = new short[to - from];
                for (long i = from; i < to; i++) r[i - from] = ring[i % ring.Length];
                return r;
            }
        }

        public void Start() { new Thread(Run) { IsBackground = true }.Start(); }
        public void Stop() { stop = true; }

        void Run()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0, 0, out dev)); // salida (eRender), consola
                Guid iidClient = typeof(IAudioClient).GUID;
                object o;
                Marshal.ThrowExceptionForHR(dev.Activate(ref iidClient, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out o));
                var client = (IAudioClient)o;
                IntPtr fmt;
                Marshal.ThrowExceptionForHR(client.GetMixFormat(out fmt));
                Channels = Marshal.ReadInt16(fmt, 2); Rate = Marshal.ReadInt32(fmt, 4); Bits = Marshal.ReadInt16(fmt, 14);
                short tag = Marshal.ReadInt16(fmt, 0);
                bool isFloat = tag == 3 || (tag == unchecked((short)0xFFFE) && Bits == 32); // extensible: casi siempre float de 32 bits
                Marshal.ThrowExceptionForHR(client.Initialize(0, 0x00020000 /*LOOPBACK*/, 2000000 /*200 ms*/, 0, fmt, IntPtr.Zero));
                Guid iidCap = typeof(IAudioCaptureClient).GUID;
                Marshal.ThrowExceptionForHR(client.GetService(ref iidCap, out o));
                var cap = (IAudioCaptureClient)o;
                Marshal.ThrowExceptionForHR(client.Start());
                int bpf = Channels * Bits / 8;
                double step = Rate / 16000.0, acc = 0; double sum = 0; int cnt = 0;
                var lastData = DateTime.UtcNow;
                while (!stop)
                {
                    Thread.Sleep(15);
                    int packet;
                    cap.GetNextPacketSize(out packet);
                    if (packet == 0)
                    {
                        // sin nada sonando Windows no manda paquetes: se rellena con silencio para que el tiempo avance
                        var now = DateTime.UtcNow;
                        int gap = (int)((now - lastData).TotalMilliseconds * 16);
                        if (gap > 1600) { lock (lk) { for (int i = 0; i < gap; i++) ring[written++ % ring.Length] = 0; } lastData = now; }
                        continue;
                    }
                    while (packet > 0)
                    {
                        IntPtr data; int frames, flags; long dp, qp;
                        Marshal.ThrowExceptionForHR(cap.GetBuffer(out data, out frames, out flags, out dp, out qp));
                        bool silent = (flags & 2) != 0;
                        lock (lk)
                        {
                            for (int f = 0; f < frames; f++)
                            {
                                double v = 0;
                                if (!silent)
                                    for (int c = 0; c < Channels; c++)
                                    {
                                        int off = f * bpf + c * Bits / 8;
                                        v += isFloat ? BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, off)), 0)
                                                     : (Bits == 16 ? Marshal.ReadInt16(data, off) / 32768.0 : Marshal.ReadInt32(data, off) / 2147483648.0);
                                    }
                                sum += v / Channels; cnt++; acc += 1;
                                if (acc >= step)
                                {
                                    // promedio de las muestras del tramo (filtro simple) -> 16 kHz
                                    acc -= step;
                                    double s = sum / cnt; sum = 0; cnt = 0;
                                    ring[written++ % ring.Length] = (short)Math.Max(-32768, Math.Min(32767, s * 32767));
                                }
                            }
                        }
                        cap.ReleaseBuffer(frames);
                        cap.GetNextPacketSize(out packet);
                    }
                    lastData = DateTime.UtcNow;
                }
                client.Stop();
            }
            catch (Exception ex) { LastError = ex.Message; }
        }
    }

    // corta lo que suena en frases por energia y las transcribe (prueba del modo "escuchar Discord")
    static class ListenTest
    {
        public static void Run(string dir, int seconds, string outPath)
        {
            var cfg = Cfg.Load(Path.Combine(dir, "config.ini"));
            int port = cfg.GetInt("Puerto", 8765);
            var sb = new StringBuilder();
            Action<string> W = s => { lock (sb) { sb.Append(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n"); File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8); } };
            if (!Whisper.Ping(port)) { W("Whisper no responde en el puerto " + port + " (abrí el overlay primero)"); return; }
            var cat = Catalog.Load(Path.Combine(dir, "datos"));
            var st = new State(cat) { AliasPath = Path.Combine(dir, "apodos.txt") };
            st.ReloadAliases();
            string prompt = st.Prompt();
            var lb = new Loopback();
            lb.Start();
            Thread.Sleep(1000);
            if (lb.LastError.Length > 0) { W("ERROR al escuchar la salida de audio: " + lb.LastError); return; }
            W("escuchando la salida de audio: " + lb.Rate + " Hz, " + lb.Channels + " canales, " + lb.Bits + " bits · " + seconds + " s");
            var me = Process.GetCurrentProcess();
            Process wp = null; try { wp = Process.GetProcessesByName("whisper-server").FirstOrDefault(); } catch { }
            double cpu0 = me.TotalProcessorTime.TotalMilliseconds, wcpu0 = wp != null ? wp.TotalProcessorTime.TotalMilliseconds : 0;
            var t0 = DateTime.Now;
            long pos = lb.Pos, segStart = -1, pos0 = lb.Pos; int quiet = 0; int segs = 0, sent = 0, hits = 0; double busyMs = 0, audioMs = 0, maxRms = 0;
            const int frame = 320; // 20 ms
            var tieneRx = new Regex(@"\b(tiene|trajo|quemo|ofrenda|tenia)\b.*$");
            while ((DateTime.Now - t0).TotalSeconds < seconds)
            {
                Thread.Sleep(100);
                long end = lb.Pos;
                while (pos + frame <= end)
                {
                    var f = lb.Get(pos, pos + frame);
                    double e = 0; foreach (var x in f) e += (double)x * x;
                    double rms = Math.Sqrt(e / Math.Max(1, f.Length));
                    bool loud = rms > 500; if (rms > maxRms) maxRms = rms;
                    if (segStart < 0) { if (loud) { segStart = Math.Max(0, pos - 16000 * 3 / 10); quiet = 0; } }
                    else
                    {
                        quiet = loud ? 0 : quiet + 1;
                        long len = pos - segStart;
                        if (quiet >= 30 || len >= 16000 * 8) // 0,6 s de silencio o 8 s maximo
                        {
                            var seg = lb.Get(segStart, pos);
                            segStart = -1; segs++;
                            if (seg.Length >= 16000 * 6 / 10)
                            {
                                sent++;
                                var ts = DateTime.Now;
                                string text;
                                try { text = Whisper.Transcribe(port, Whisper.Wav(seg), prompt); } catch (Exception ex) { text = "ERROR " + ex.Message; }
                                double ms = (DateTime.Now - ts).TotalMilliseconds; busyMs += ms; audioMs += seg.Length / 16.0;
                                string nt = Txt.Norm(text ?? "");
                                var m = tieneRx.Match(nt);
                                string res = "";
                                if (m.Success)
                                {
                                    st.Trace.Clear();
                                    res = st.Apply("creo que " + m.Value); // lo que escucha de otros entra como sospecha
                                    if (!Lang.Fail(res)) hits++;
                                }
                                W((seg.Length / 16000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s, Whisper " + (int)ms + " ms: \"" + (text ?? "").Trim() + "\"" + (m.Success ? "  ->  " + res : ""));
                            }
                        }
                    }
                    pos += frame;
                }
            }
            lb.Stop();
            double wall = (DateTime.Now - t0).TotalMilliseconds;
            double cpu = me.TotalProcessorTime.TotalMilliseconds - cpu0;
            double wcpu = 0; try { if (wp != null) { wp.Refresh(); wcpu = wp.TotalProcessorTime.TotalMilliseconds - wcpu0; } } catch { }
            int cores = Environment.ProcessorCount;
            W("---- RESUMEN ----");
            W("captado: " + ((lb.Pos - pos0) / 16000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s de audio, volumen maximo " + (int)maxRms + " (umbral 500)");
            W("tramos con sonido: " + segs + ", mandados a Whisper: " + sent + ", frases anotables: " + hits);
            W("audio transcripto: " + (audioMs / 1000).ToString("0", CultureInfo.InvariantCulture) + " s de " + (wall / 1000).ToString("0", CultureInfo.InvariantCulture) + " s; Whisper ocupado " + (100 * busyMs / wall).ToString("0.0", CultureInfo.InvariantCulture) + "% del tiempo");
            W("procesador: esta prueba " + (100 * cpu / wall / cores).ToString("0.00", CultureInfo.InvariantCulture) + "% del total, Whisper " + (100 * wcpu / wall / cores).ToString("0.00", CultureInfo.InvariantCulture) + "% del total (" + cores + " hilos)");
            W("estado que habria quedado (todo como sospecha): " + st.Describe());
        }
    }

    // ------------------------------------------------------------ builds de hens333.com/killerbuilds
    // Solo se baja la pagina del killer anotado, y como mucho una vez cada 3 dias (nada en segundo plano ni periodico).
    // Se guarda en datos/hens_builds.txt: "killer<tab>fecha<tab>parche<tab>perk,perk,…<tab>addon,…" (una linea por build).
    static class HensBuilds
    {
        static readonly object lk = new object();
        static string dir;
        static Dictionary<string, List<string[]>> cache;
        static HashSet<string> used = new HashSet<string>();             // perks que aparecen en alguna build guardada
        static readonly HashSet<string> busy = new HashSet<string>();
        static readonly Dictionary<string, DateTime> failed = new Dictionary<string, DateTime>();
        public static string Slug(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant().Replace("'", "").Replace("’", "").Normalize(NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString(), "[^a-z0-9]+", "-").Trim('-');
        }
        // perks que la pagina de Hens nombra y que todavia no estan en los datos de la app (capitulo nuevo): slug -> { nombre, dueño }
        static Dictionary<string, string[]> perkInfo;
        public static List<string[]> PerkInfo() { lock (lk) return perkInfo == null ? new List<string[]>() : perkInfo.Select(kv => new[] { kv.Key, kv.Value[0], kv.Value[1] }).ToList(); }
        // "scourge-hook-pain-resonance" = "Scourge Hook: Pain Resonance", "barbecue-and-chilli" = "Barbecue & Chili"
        public static string Loose(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant(), @"^(hex|scourge[- ]hook|boon|invocation)[-:]\s*", "");
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD)) if (c >= 'a' && c <= 'z') sb.Append(c);
            return Regex.Replace(sb.ToString().Replace("and", ""), @"(.)\1+", "$1");
        }
        public static void Init(string d)
        {
            lock (lk)
            {
                if (cache != null) return;
                dir = d;
                cache = new Dictionary<string, List<string[]>>();
                try
                {
                    string path = Path.Combine(dir, "hens_builds.txt");
                    if (File.Exists(path))
                        foreach (var l in File.ReadAllLines(path, Encoding.UTF8))
                        {
                            var f = l.Split('\t'); if (f.Length < 5) continue;
                            List<string[]> li; if (!cache.TryGetValue(f[0], out li)) cache[f[0]] = li = new List<string[]>();
                            li.Add(f);
                        }
                }
                catch { }
                perkInfo = new Dictionary<string, string[]>();
                try
                {
                    string pp = Path.Combine(dir, "hens_perks.txt");
                    if (File.Exists(pp))
                        foreach (var l in File.ReadAllLines(pp, Encoding.UTF8)) { var f = l.Split('\t'); if (f.Length >= 3) perkInfo[f[0]] = new[] { f[1], f[2] }; }
                }
                catch { }
                RebuildUsed();
            }
        }
        static void RebuildUsed()
        {
            used = new HashSet<string>(cache.Values.SelectMany(v => v).SelectMany(f => f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)).Select(Loose));
        }
        public static bool IsUsed(string perkName) { return used.Contains(Loose(perkName)); }
        // lo guardado para ese killer (null si nada). Si esta viejo o falta, lo baja en segundo plano y despues llama a done.
        public static List<string[]> Get(string slug, Action done)
        {
            List<string[]> li;
            lock (lk)
            {
                if (cache == null) return null;
                cache.TryGetValue(slug, out li);
                long t; DateTime ft;
                bool fresh = li != null && li.Count > 0 && long.TryParse(li[0][1], out t) && (DateTime.UtcNow - new DateTime(t, DateTimeKind.Utc)).TotalDays < 3;
                bool wait = failed.TryGetValue(slug, out ft) && (DateTime.UtcNow - ft).TotalMinutes < 10; // sin internet: no reintentar enseguida
                if (fresh || wait || busy.Contains(slug)) return li;
                busy.Add(slug);
            }
            new Thread(() =>
            {
                bool ok = false;
                try
                {
                    string html = Encoding.UTF8.GetString(Catalog.HttpBytes(Catalog.HensBase + "killerbuilds/" + slug + "/"));
                    var pm = Regex.Match(html, @"patch\s+([0-9][0-9a-z.]*)");
                    string now = DateTime.UtcNow.Ticks.ToString(), patch = pm.Success ? pm.Groups[1].Value : "";
                    var rows = new List<string[]>();
                    foreach (Match m in Regex.Matches(html, @"b/new#r=k&(?:amp;)?k=([a-z0-9-]+)&(?:amp;)?p=([a-z0-9,-]*)&(?:amp;)?a=([a-z0-9,-]*)"))
                        if (m.Groups[1].Value == slug && !rows.Any(x => x[3] == m.Groups[2].Value))
                            rows.Add(new[] { slug, now, patch, m.Groups[2].Value, m.Groups[3].Value });
                    if (rows.Count == 0) rows.Add(new[] { slug, now, patch, "", "" }); // todavia sin builds ("coming soon")
                    // nombre y dueño de cada perk (para los que la app todavia no conoce)
                    var info = new Dictionary<string, string[]>();
                    foreach (var chunk in html.Split(new[] { "<details class=\"spec\"" }, StringSplitOptions.None).Skip(1))
                    {
                        var pm2 = Regex.Match(chunk, @"/wiki/perks/([a-z0-9-]+)\.webp");
                        var nm = Regex.Match(chunk, @"class=""spec-name""[^>]*>([^<]+)<");
                        var ow = Regex.Match(chunk, @"class=""spec-owner[^""]*""[^>]*>([^<]*)<");
                        if (pm2.Success && nm.Success)
                            info[pm2.Groups[1].Value] = new[] { WebUtility.HtmlDecode(nm.Groups[1].Value).Trim(), ow.Success ? WebUtility.HtmlDecode(ow.Groups[1].Value).Trim() : "" };
                    }
                    lock (lk)
                    {
                        cache[slug] = rows;
                        foreach (var kv in info) perkInfo[kv.Key] = kv.Value;
                        File.WriteAllLines(Path.Combine(dir, "hens_perks.txt"), perkInfo.Select(kv => kv.Key + "\t" + kv.Value[0] + "\t" + kv.Value[1]), Encoding.UTF8);
                        RebuildUsed();
                        File.WriteAllLines(Path.Combine(dir, "hens_builds.txt"), cache.Values.SelectMany(v => v).Select(f => string.Join("\t", f)), Encoding.UTF8);
                    }
                    ok = true;
                }
                catch { lock (lk) failed[slug] = DateTime.UtcNow; }
                finally { lock (lk) busy.Remove(slug); }
                if (ok && done != null) done();
            }) { IsBackground = true }.Start();
            return li;
        }
    }

    // ------------------------------------------------------------ actualizaciones desde GitHub (firmadas)
    static class Updater
    {
        // publicar.ps1 sube este numero en cada version
        public const int AppVersion = 46;
        public const string Base = "https://raw.githubusercontent.com/EzChe1997/CalloutsInTheFog/main/";
        // solo se aceptan versiones firmadas con la clave privada (que queda en la PC de quien publica)
        const string PublicKey = "<RSAKeyValue><Modulus>qWTK5n2m/nN/lqDh5zLLW1oiHJdRe/cC7tMzyMZCtKGh8dYbyKOvwDfQaNFVmqniOG9pYfP38o+QCnkjeaone7gXnt4H6M6DjB7F+ECvTBBH09TbDrrHGUKWNQryiwG0IeS0US8/iQfzOLiqeDamsyCbf3n1CHwVjOcK3G2Dhv6A0cX/vl3R1jLUAhTk5lfxzSF58la4VbGJsAEWH/943Lmy+BYj7pAVaPMkNy+1FiUmyejlWIIEBAn60CLhFY0jtyWVvyUt2wEqd3B7oKDgCRKGGEegmLp0n7yF3sHxXAfpyQd7VbrY/Tn5Hl6VybQ+1gmiTWFi2KiP1ZzA7fvg/Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // busca una version nueva; si la hay la baja, verifica firma y huella, y deja el exe nuevo en su lugar.
        // Devuelve la descripcion de la version instalada, o null si no habia nada nuevo.
        public static string CheckAndInstall(out string error)
        {
            error = null;
            try
            {
                // GitHub guarda copias de los archivos hasta 5 min: se pide el ultimo commit y se bajan los archivos
                // de ESE commit (direccion nueva = nunca una copia vieja). Si falla, la direccion de siempre.
                string ub = Base;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/EzChe1997/CalloutsInTheFog/commits/main");
                    req.UserAgent = "DbdOverlay"; req.Accept = "application/vnd.github.sha"; req.Timeout = 10000;
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    string shaC;
                    using (var r = req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream())) shaC = sr.ReadToEnd().Trim();
                    if (Regex.IsMatch(shaC, "^[0-9a-f]{40}$")) ub = Base.Replace("/main/", "/" + shaC + "/");
                }
                catch { }
                byte[] man = Catalog.HttpBytes(ub + "update.json?t=" + DateTime.UtcNow.Ticks);
                string sig = Encoding.ASCII.GetString(Catalog.HttpBytes(ub + "update.sig?t=" + DateTime.UtcNow.Ticks)).Trim();
                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKey);
                    if (!rsa.VerifyData(man, "SHA256", Convert.FromBase64String(sig))) { error = Lang.L("firma inválida (se ignora)", "invalid signature (ignored)"); return null; }
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(man).TrimStart('﻿'));
                int ver = Convert.ToInt32(d["version"]);
                if (ver <= AppVersion) return null;
                var files = (Dictionary<string, object>)d["files"];
                string want = files["DbdOverlay.exe"].ToString();
                byte[] exe = Catalog.HttpBytes(ub + "DbdOverlay.exe?v=" + ver);
                string got;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    got = string.Concat(sha.ComputeHash(exe).Select(b => b.ToString("x2")));
                if (got != want) { error = Lang.L("el archivo bajado no coincide con la firma (se ignora)", "the downloaded file doesn't match the signature (ignored)"); return null; }
                // Windows deja renombrar el exe que esta corriendo: el viejo queda como .old y el nuevo toma su lugar
                string me = Application.ExecutablePath, old = me + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(me, old);
                try { File.WriteAllBytes(me, exe); }
                catch { File.Move(old, me); throw; }
                object notes; d.TryGetValue("notes", out notes);
                return Lang.L("versión ", "version ") + ver + ((notes ?? "").ToString().Length > 0 && !Lang.En ? ": " + notes : "");
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static void CleanOld() { try { var old = Application.ExecutablePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { } }
    }

    // ------------------------------------------------------------ whisper-server
    static class Whisper
    {
        public static bool Ping(int port)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/");
                req.Proxy = null; req.Timeout = 1500;
                using (var r = (HttpWebResponse)req.GetResponse()) return r.StatusCode == HttpStatusCode.OK;
            }
            catch { return false; }
        }

        public static byte[] Wav(short[] s)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int data = s.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
            foreach (var x in s) w.Write(x);
            w.Flush();
            return ms.ToArray();
        }

        public static string Transcribe(int port, byte[] wav, string prompt)
        {
            string bnd = "----dbd" + DateTime.Now.Ticks.ToString("x");
            var body = new MemoryStream();
            Action<string> W = str => { var b = Encoding.UTF8.GetBytes(str); body.Write(b, 0, b.Length); };
            Action<string, string> Field = (k, v) => W("--" + bnd + "\r\nContent-Disposition: form-data; name=\"" + k + "\"\r\n\r\n" + v + "\r\n");
            Field("response_format", "json");
            Field("language", Lang.En ? "en" : "es");
            Field("temperature", "0");
            Field("prompt", prompt);
            W("--" + bnd + "\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.wav\"\r\nContent-Type: audio/wav\r\n\r\n");
            body.Write(wav, 0, wav.Length);
            W("\r\n--" + bnd + "--\r\n");

            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/inference");
            req.Proxy = null; req.Method = "POST"; req.Timeout = 30000;
            req.ContentType = "multipart/form-data; boundary=" + bnd;
            var bytes = body.ToArray();
            req.ContentLength = bytes.Length;
            using (var rs = req.GetRequestStream()) rs.Write(bytes, 0, bytes.Length);
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(sr.ReadToEnd());
                object t;
                return d.TryGetValue("text", out t) && t != null ? t.ToString().Trim() : "";
            }
        }
    }

    // ------------------------------------------------------------ overlay
    class Overlay : Form
    {
        public static string L(string es, string en) { return Lang.L(es, en); }
        public const string AppName = "Callouts in the Fog";
        static Overlay() { } // asi los textos fijos se arman recien al usar la clase (despues de elegir el idioma)
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        readonly string dir;
        Cfg cfg; Catalog cat; State st; Mic mic;
        int port, vkTalk, vkClear, vkNames, vkHelp;
        bool talkShift, talkCtrl, talkAlt;
        bool helpShown;
        // ---- historial de lo visto (vistos.txt, solo en esta PC): al borrar la lista se guarda killer + perks.
        // Las ultimas 300 partidas: se adapta solo al meta y a los parches (lo viejo se va yendo).
        string SeenPath { get { return Path.Combine(dir, "vistos.txt"); } }
        bool recordedLast;
        void RecordMatch()
        {
            recordedLast = false;
            if (st.Perks.Count == 0) return;
            try
            {
                string line = DateTime.UtcNow.ToString("yyyy-MM-dd") + "\t" + (st.Killer != null ? st.Killer.Name : "") + "\t" +
                              string.Join("|", st.Perks.Select(p => (st.IsDoubt(p) ? "?" : "") + p.Name));
                var lines = File.Exists(SeenPath) ? File.ReadAllLines(SeenPath, Encoding.UTF8).ToList() : new List<string>();
                lines.Add(line);
                if (lines.Count > 300) lines = lines.Skip(lines.Count - 300).ToList();
                File.WriteAllLines(SeenPath, lines, Encoding.UTF8);
                recordedLast = true;
            }
            catch { }
        }
        void UnrecordLast()
        {
            if (!recordedLast) return;
            recordedLast = false;
            try { var l = File.ReadAllLines(SeenPath, Encoding.UTF8).ToList(); if (l.Count > 0) { l.RemoveAt(l.Count - 1); File.WriteAllLines(SeenPath, l, Encoding.UTF8); } } catch { }
        }

        // cuadro vacio: perks vistos contra este killer, despues los mas vistos en general, despues los propios del killer
        public List<Entry> QuickPicks(out string title)
        {
            var k = st.Killer;
            title = k != null ? L("Lo más visto contra ", "Most seen against ") + DName(k) : L("Lo más visto últimamente", "Most seen lately");
            var score = new Dictionary<Entry, double>();
            try
            {
                if (File.Exists(SeenPath))
                {
                    var lines = File.ReadAllLines(SeenPath, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var f = lines[i].Split('\t');
                        if (f.Length < 3) continue;
                        double w = 1 + (double)i / lines.Length; // lo reciente pesa mas
                        if (k != null && f[1] == k.Name) w *= 4;  // contra el mismo killer, mucho mas
                        foreach (var raw in f[2].Split('|'))
                        {
                            bool doubt = raw.StartsWith("?");
                            string nm = doubt ? raw.Substring(1) : raw;
                            var e = cat.Perks.FirstOrDefault(x => x.Name == nm);
                            if (e == null) continue;
                            double v; score.TryGetValue(e, out v);
                            score[e] = v + (doubt ? w / 2 : w);
                        }
                    }
                }
            }
            catch { }
            var res = score.Where(x => !st.Perks.Contains(x.Key)).OrderByDescending(x => x.Value).Select(x => x.Key).Take(10).ToList();
            if (k != null)
                foreach (var own in cat.Perks.Where(x => x.Owner == k.Name).OrderBy(x => DName(x)))
                    if (!res.Contains(own) && !st.Perks.Contains(own)) res.Add(own);
            if (score.Count == 0 && k != null) title = Lang.En ? DName(k) + " perks" : "Perks de " + DName(k);
            return res;
        }

        public string HelpKeyName { get { return cfg.Get("TeclaAyuda", "Shift+H"); } }
        string TypeKeyText { get { return cfg.Get("TeclaEscribir", "Shift+T"); } }
        string MapOcrKeyText
        {
            get
            {
                return Regex.Replace(cfg.Get("TeclaMapaFoto", "MButton"), "MButton|XButton1|XButton2", m =>
                    m.Value == "MButton" ? L("Clic del medio", "Middle click") : m.Value == "XButton1" ? L("Botón lateral 1", "Side button 1") : L("Botón lateral 2", "Side button 2"));
            }
        }

        // texto, color, es titulo
        List<Tuple<string, Color, bool>> HelpLines()
        {
            Color head = Color.FromArgb(235, 90, 90), cmd = Color.WhiteSmoke, note = Color.FromArgb(150, 150, 160);
            var l = new List<Tuple<string, Color, bool>>();
            Action<string, Color, bool> A = (t, c, b) => l.Add(Tuple.Create(t, c, b));
            // primero: si estas compartiendo la lista y con quien
            if (!Preview)
            {
                string who = partyNames.Count > 0 ? ": " + string.Join(", ", partyNames.Values.Where(n => n.Length > 0).Distinct()) : "";
                A((InGroup ? L("👥 En grupo", "👥 In a group") + who : L("Sin grupo", "No group")) + " · " + RoomText(), InGroup ? Color.FromArgb(130, 200, 140) : note, false);
            }
            if (Lang.En)
            {
                A("Type: " + TypeKeyText, head, true);
                A("the name (or part of it) of a perk, add-on, offering, killer or map, then Enter", cmd, false);
                A("\"noed?\" or Shift+Enter   adds it as a guess (?)", cmd, false);
                A("\"addon 15 slow\" · \"perk aura hook\"   search by what it does", cmd, false);
                A("\"remove …\" · \"change …\" · \"doubt …\" · \"room …\"", cmd, false);
                if (voice)
                {
                    A("Hold " + KeyName + " and say:", head, true);
                    A("\"he has Pain Resonance and Deadlock\"   adds perks (max. 4)", cmd, false);
                    A("\"the killer is the Huntress\"   killer (helps with add-ons)", cmd, false);
                    A("\"add-on that blinds\"   add-on by what it does (max. 2); if unsure: \"number two\" or \"none\"", cmd, false);
                    A("\"remove Deadlock\" · \"undo\" · \"yes\" when it asks (…?)", cmd, false);
                    A("\"offering Ebony Mori\" · \"I think he has Deadlock\" (guess) · \"confirmed\" · \"discarded\"", cmd, false);
                }
                A("Keys", head, true);
                A(ClearKeyName + " (hold 1 s)   clear everything · tap again right after = undo", cmd, false);
                A(HelpKeyName + " (hold)   this help", cmd, false);
                if (vkMapOcr != 0) A(MapOcrKeyText + "   reads the screen: the map name (bottom-left, at the start) and perks in the status alerts (top-right)", cmd, false);
                if (!voice) A("Voice is not installed (optional): Settings → Voice", note, false);
                A(RoomText() + (room != null ? " · everyone sees the same list" : ""), note, false);
                A("Settings: double-click the hook icon in the taskbar", note, false);
                return l;
            }
            A("Escribir: " + TypeKeyText, head, true);
            A("el nombre (o una parte) de un perk, addon, ofrenda, killer o mapa, y Enter", cmd, false);
            A("\"noed?\" o Shift+Enter   lo anota como sospecha (?)", cmd, false);
            A("\"addon 15 lento\" · \"perk aura gancho\"   busca por lo que hace", cmd, false);
            A("\"quitar …\" · \"cambiar …\" · \"duda …\" · \"sala …\"", cmd, false);
            if (voice)
            {
                A("Mantené " + KeyName + " y decí:", head, true);
                A("\"tiene Pain Resonance y Deadlock\"   anota perks (máx. 4)", cmd, false);
                A("\"es la Huntress\"   killer (ayuda con los addons)", cmd, false);
                A("\"addon que ciega\"   addon por lo que hace (máx. 2); si duda: \"el dos\" o \"ninguno\"", cmd, false);
                A("\"quitar Deadlock\" · \"no, borrá eso\" · \"sí\" cuando pregunta (¿...?)", cmd, false);
                A("\"ofrenda Ebony Mori\" · \"creo que tiene Deadlock\" (sospecha) · \"confirmado\" · \"descartado\"", cmd, false);
                A("\"apodo grimi es Grim Embrace\" · \"mostrar nombres\" · \"ocultar nombres\"", cmd, false);
            }
            A("Teclas", head, true);
            A(ClearKeyName + " (mantener 1 s)   borrar todo · un toque después = deshacer", cmd, false);
            A(HelpKeyName + " (mantener)   esta ayuda", cmd, false);
            if (vkMapOcr != 0) A(MapOcrKeyText + "   lee la pantalla: el mapa (abajo a la izq., al empezar) y los perks de los avisos de estado (arriba a la der.)", cmd, false);
            if (!voice) A("La voz no está instalada (es opcional): Configuración → Voz", note, false);
            A(RoomText() + (room != null ? " · todos ven lo mismo" : ""), note, false);
            A("Configuración: doble clic en el ícono del gancho en la barra de tareas", note, false);
            return l;
        }
        string statusLine = "";
        bool serverReady, talking, clearDown, namesDown, busy;
        ToolStripMenuItem namesItem;
        long recStart; int releaseAt = -1;
        string feedback = ""; Color feedbackColor = Color.Silver; int feedbackUntil;
        int micRestartAt = -1;
        NotifyIcon tray;
        Font fTitle, fItem, fSmall;



        // ---- iconos (se bajan de la wiki la primera vez y quedan en iconos\)
        readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();
        readonly HashSet<string> iconPending = new HashSet<string>();
        int IconSize { get { return (int)(fItem.Height * 1.75); } }

        // ---- plano del mapa (arriba a la izquierda): se elige escribiendo el nombre del mapa en el cuadro.
        // Se va cuando se borra la lista (partida nueva) o con "mapa" -> "Ocultar mapa". Se comparte con la sala.
        MapWindow mapWin;
        string mapShown;

        public void ShowMap(string name, bool share)
        {
            List<string> files;
            if (!cat.MapFiles.TryGetValue(name, out files)) return;
            if (mapWin == null) mapWin = new MapWindow(this);
            mapShown = name;
            var e = cat.Maps.FirstOrDefault(m => m.Name == name);
            mapWin.Set(name, e != null ? e.ActionTag : "", files, MapSize);
            mapWin.Place(cfg.GetInt("MapaX", 20), cfg.GetInt("MapaY", 20));
            mapWin.Opacity = SafeOp(MapOpacity / 100.0);
            UpdateMapVisibility(cursorShown);
            if (mapWin.Visible) SetWindowPos(mapWin.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            mapWin.Invalidate();
            DebugEvent("mapa: " + name);
            if (share && room != null) room.Publish(Stamp(new Dictionary<string, object> { { "map", name } }));
        }

        static readonly Tuple<string, int>[] MapSizes = { Tuple.Create("Chico", 160), Tuple.Create("Normal", 220), Tuple.Create("Grande", 280), Tuple.Create("Muy grande", 360), Tuple.Create("Enorme", 460) };
        int MapSize { get { return cfg.GetInt("TamanoMapa", 220); } }
        public void SetMapSize(int px)
        {
            px = Math.Max(160, Math.Min(900, px));
            SetCfg("TamanoMapa", px.ToString());
            if (mapShown != null) ShowMap(mapShown, false);
            Feedback(L("tamaño del mapa: ", "map size: ") + px + " px", Color.Silver, 3000);
        }
        // un paso mas grande / mas chico (desde el cuadro de escribir)
        public void StepMapSize(int dir)
        {
            int cur = MapSize;
            var next = dir > 0 ? MapSizes.Where(x => x.Item2 > cur).Select(x => x.Item2).DefaultIfEmpty(MapSizes.Last().Item2).First()
                               : MapSizes.Where(x => x.Item2 < cur).Select(x => x.Item2).DefaultIfEmpty(MapSizes[0].Item2).Last();
            SetMapSize(next);
        }

        static readonly int[] MapOpacities = { 30, 45, 60, 75, 90, 100 };
        // las ventanas que dejan pasar los clics son "layered": con opacidad 100 % exacta WinForms no le pone el alfa y queda invisible
        internal static double SafeOp(double v) { return Math.Max(0.05, Math.Min(0.996, v)); }
        int MapOpacity { get { return Math.Max(15, Math.Min(100, cfg.GetInt("OpacidadMapa", 90))); } }
        public void SetMapOpacity(int pc)
        {
            pc = Math.Max(15, Math.Min(100, pc));
            SetCfg("OpacidadMapa", pc.ToString());
            if (mapWin != null) mapWin.Opacity = SafeOp(pc / 100.0);
            Feedback(L("opacidad del mapa: ", "map opacity: ") + pc + " %", Color.Silver, 3000);
        }
        public void StepMapOpacity(int dir)
        {
            int cur = MapOpacity;
            SetMapOpacity(dir > 0 ? MapOpacities.Where(x => x > cur).DefaultIfEmpty(MapOpacities.Last()).First()
                                  : MapOpacities.Where(x => x < cur).DefaultIfEmpty(MapOpacities[0]).Last());
        }

        public void HideMap(bool share)
        {
            if (mapShown == null) return;
            mapShown = null;
            if (mapWin != null) mapWin.Hide();
            if (share && room != null) room.Publish(Stamp(new Dictionary<string, object> { { "map", "" } }));
        }

        // ---- marcas en el plano (prototipo, solo Coal Tower): un punto que ven todos + donde queda, en palabras ----
        internal bool MapMarksOn { get { return cfg.Get("MapaMarcas", "no") == "si"; } }
        internal class MapRef { public string Name; public float X, Y; public MapRef(string n, float x, float y) { Name = n; X = x; Y = y; } }
        internal class MapGuide { public MapRef Main, Shack, Side; public List<MapRef> Others = new List<MapRef>(), Clock = new List<MapRef>(); public RectangleF Area; }
        // coordenadas en pixeles del plano de hens (900 de ancho, como queda guardado)
        internal static readonly Dictionary<string, MapGuide> MapGuides = new Dictionary<string, MapGuide> {
            { "Coal Tower", new MapGuide {
                Area = new RectangleF(140, 110, 615, 860),
                Main = new MapRef("Main", 449, 251), Shack = new MapRef("Shack", 504, 880), Side = new MapRef("WT", 354, 627),
                Others = new List<MapRef> { new MapRef("WT", 354, 627) },
                Clock = new List<MapRef> { new MapRef("12", 455, 145), new MapRef("1", 690, 165), new MapRef("2", 703, 322), new MapRef("3", 704, 480),
                    new MapRef("4", 697, 637), new MapRef("5", 682, 790), new MapRef("6", 352, 915), new MapRef("7", 212, 778), new MapRef("8", 212, 612),
                    new MapRef("9", 212, 467), new MapRef("10", 222, 312), new MapRef("11", 222, 168) } } } };
        static float Dist(MapRef r, float x, float y) { return (float)Math.Sqrt((r.X - x) * (r.X - x) + (r.Y - y) * (r.Y - y)); }
        // "entre Main y Shack, del lado de WT, contra el borde" (+ el numero del reloj, para quien lo use)
        internal static string DescribeSpot(MapGuide g, float x, float y)
        {
            if (!g.Area.Contains(x, y)) return null;
            float size = (float)Math.Sqrt(g.Area.Width * g.Area.Width + g.Area.Height * g.Area.Height);
            var parts = new List<string>();
            MapRef near = null; float nd = float.MaxValue;
            foreach (var r in new[] { g.Main, g.Shack }.Concat(g.Others)) { float d = Dist(r, x, y); if (d < nd) { nd = d; near = r; } }
            if (nd < size * 0.07f) parts.Add(L("en ", "at ") + near.Name);
            else
            {
                float ax = g.Shack.X - g.Main.X, ay = g.Shack.Y - g.Main.Y, len2 = ax * ax + ay * ay, len = (float)Math.Sqrt(len2);
                float t = ((x - g.Main.X) * ax + (y - g.Main.Y) * ay) / len2;
                if (t < -0.1f) parts.Add(L("detrás de Main (lejos de Shack)", "behind Main (away from Shack)"));
                else if (t < 0.3f) parts.Add(L("cerca de Main", "near Main"));
                else if (t <= 0.7f) parts.Add(L("entre Main y Shack", "between Main and Shack"));
                else if (t <= 1.1f) parts.Add(L("cerca de Shack", "near Shack"));
                else parts.Add(L("detrás de Shack (lejos de Main)", "behind Shack (away from Main)"));
                float side = ((x - g.Main.X) * ay - (y - g.Main.Y) * ax) / len;
                if (Math.Abs(side) <= size * 0.1f) parts.Add(L("por el medio", "through the middle"));
                else if (g.Side != null)
                {
                    float rs = (g.Side.X - g.Main.X) * ay - (g.Side.Y - g.Main.Y) * ax;
                    parts.Add(Math.Sign(rs) == Math.Sign(side) ? L("del lado de ", "on the side of ") + g.Side.Name : L("del lado opuesto a ", "on the side away from ") + g.Side.Name);
                }
            }
            float m = size * 0.085f;
            if (x - g.Area.Left < m || g.Area.Right - x < m || y - g.Area.Top < m || g.Area.Bottom - y < m) parts.Add(L("contra el borde", "against the edge"));
            string s = string.Join(", ", parts);
            var ck = g.Clock.OrderBy(r => Dist(r, x, y)).FirstOrDefault();
            if (ck != null && Dist(ck, x, y) < size * 0.15f) s += "   (" + ck.Name + ")";
            return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
        }
        // marca propia: la ven todos los de la sala
        internal void PublishMark(float x, float y, bool clear)
        {
            if (mapShown == null) return;
            if (room != null) room.Publish(Stamp(new Dictionary<string, object> { { "mark", clear ? "" : ((int)x) + "," + ((int)y) }, { "mapn", mapShown } }));
            if (!clear) DebugEvent("marca en el plano: " + DescribeSpot(MapGuides[mapShown], x, y));
        }

        class MapWindow : Form
        {
            readonly Overlay ov;
            string title = "", sub = "";
            List<string> files = new List<string>();
            int side = 320, px, py, imgH = 320;
            const int Strip = 44;
            // marcas por persona (una cada uno), en pixeles del plano
            readonly Dictionary<string, PointF> marks = new Dictionary<string, PointF>();
            string hoverText, lastText;
            MapGuide Guide { get { MapGuide g; return ov.MapMarksOn && MapGuides.TryGetValue(title, out g) ? g : null; } }
            [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
            [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
            // con el cursor visible (menu ESC) y las marcas activadas, el plano recibe clics; si no, el mouse lo atraviesa como siempre
            public void SetClickable(bool on)
            {
                on = on && Guide != null;
                int ex = GetWindowLong(Handle, -20), nx = on ? ex & ~0x20 : ex | 0x20;
                if (nx != ex) SetWindowLong(Handle, -20, nx);
                if (!on && hoverText != null) { hoverText = null; Invalidate(); }
            }
            public void SetMark(string who, PointF? p)
            {
                if (p == null) marks.Remove(who); else marks[who] = p.Value;
                var g = Guide;
                lastText = p == null || g == null ? null : who + ": " + DescribeSpot(g, p.Value.X, p.Value.Y);
                Invalidate();
            }
            PointF ToMap(Point pt)
            {
                var img = files.Count > 0 ? ov.MapImage(files[0]) : null;
                if (img == null) return new PointF(-1, -1);
                return new PointF((pt.X - 6) * img.Width / (float)side, (pt.Y - 6) * img.Height / (float)imgH);
            }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                var g = Guide; if (g == null) return;
                var p = ToMap(e.Location);
                string t = DescribeSpot(g, p.X, p.Y);
                if (t != hoverText) { hoverText = t; Invalidate(); }
            }
            protected override void OnMouseLeave(EventArgs e) { if (hoverText != null) { hoverText = null; Invalidate(); } }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                var g = Guide; if (g == null) return;
                string me = L("Vos", "You");
                if (e.Button == MouseButtons.Right) { SetMark(me, null); ov.PublishMark(0, 0, true); return; }
                if (e.Button != MouseButtons.Left) return;
                var p = ToMap(e.Location);
                if (DescribeSpot(g, p.X, p.Y) == null) return;
                SetMark(me, p);
                ov.PublishMark(p.X, p.Y, false);
            }
            public MapWindow(Overlay o)
            {
                ov = o;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
                StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(14, 14, 18);
                DoubleBuffered = true; Opacity = 0.9;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000; return cp; } // en capas, el mouse lo atraviesa, sin foco
            }
            public void Set(string t, string s, List<string> f, int size) { if (t != title) { marks.Clear(); lastText = hoverText = null; } title = t; sub = s; files = f; side = Math.Max(160, Math.Min(900, size)); }
            public void Place(int x, int y) { px = x; py = y; Fit(); }
            // alto segun la imagen (las de hens no son cuadradas)
            public void Fit()
            {
                var img = files.Count > 0 ? ov.MapImage(files[0]) : null;
                int h = img != null ? side * img.Height / Math.Max(1, img.Width) : side;
                var scr = Screen.PrimaryScreen.Bounds;
                imgH = h;
                Bounds = new Rectangle(scr.Left + px, scr.Top + py, side + 12, h + 12 + (Guide != null ? Strip : 0));
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var img = files.Count > 0 ? ov.MapImage(files[0]) : null;
                var r = new Rectangle(6, 6, side, imgH);
                if (img != null) g.DrawImage(img, r);
                var gd = Guide;
                if (gd != null && img != null)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    foreach (var kv in marks)
                    {
                        float mx = 6 + kv.Value.X * side / img.Width, my = 6 + kv.Value.Y * imgH / img.Height;
                        using (var b = new SolidBrush(Color.FromArgb(90, 230, 40, 50))) g.FillEllipse(b, mx - 16, my - 16, 32, 32);
                        using (var b = new SolidBrush(Color.FromArgb(230, 40, 50))) g.FillEllipse(b, mx - 7, my - 7, 14, 14);
                        using (var p = new Pen(Color.White, 2.5f)) g.DrawEllipse(p, mx - 7, my - 7, 14, 14);
                        using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                        {
                            int tw = TextRenderer.MeasureText(kv.Key, f).Width;
                            int lx = mx + 10 + tw > side + 4 ? (int)mx - 10 - tw : (int)mx + 10; // cerca del borde derecho: a la izquierda
                            TextRenderer.DrawText(g, kv.Key, f, new Point(lx + 1, (int)my - 7), Color.Black);
                            TextRenderer.DrawText(g, kv.Key, f, new Point(lx, (int)my - 8), Color.White);
                        }
                    }
                    // abajo: donde esta el mouse (al pasar) o la ultima marca, en palabras
                    var sr = new Rectangle(6, 6 + imgH + 2, side, Strip - 4);
                    string txt = hoverText ?? lastText ?? L("Clic en el plano: marcar para todos · derecho: borrar tu marca", "Click the map: mark for everyone · right: remove your mark");
                    using (var f = new Font("Segoe UI", hoverText != null || lastText != null ? 9.5f : 8.5f, hoverText != null || lastText != null ? FontStyle.Bold : FontStyle.Regular))
                        TextRenderer.DrawText(g, txt, f, sr, hoverText != null ? Color.FromArgb(235, 235, 240) : lastText != null ? Color.FromArgb(255, 150, 150) : Color.FromArgb(150, 150, 165),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                else if (img == null) using (var fs = new Font("Segoe UI", 9f))
                    TextRenderer.DrawText(g, ov.MapError != null ? L("No pude bajar el plano de ", "Couldn't download the map of ") + title + "\n(" + ov.MapError + ")" : L("cargando ", "loading ") + title + "…", fs, r, Color.FromArgb(160, 160, 175), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                using (var pen = new Pen(Color.FromArgb(70, 70, 90))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        // imagen del mapa (hens333.com, formato WebP): se baja una vez, se pasa a PNG (Windows abre WebP con su
        // decodificador) achicada a 900 px de ancho y queda guardada en "mapas".
        readonly Dictionary<string, Image> mapCache = new Dictionary<string, Image>();
        readonly HashSet<string> mapPending = new HashSet<string>();
        readonly Dictionary<string, DateTime> mapFailed = new Dictionary<string, DateTime>();
        public string MapError;
        static string HensMapUrl(string file)
        {
            if (file.StartsWith("/")) return Catalog.HensBase + file.Substring(1);
            return Catalog.HensBase + "img/dbd/callouts/" + string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
        }
        // WebP (RIFF), PNG, GIF o JPG; cuando la imagen no existe la web devuelve una pagina HTML
        static bool IsImageData(byte[] d)
        {
            if (d == null || d.Length < 12) return false;
            return (d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F') || (d[0] == 0x89 && d[1] == 'P') || (d[0] == 'G' && d[1] == 'I' && d[2] == 'F') || (d[0] == 0xFF && d[1] == 0xD8);
        }
        public Image MapImage(string file)
        {
            Image img;
            if (mapCache.TryGetValue(file, out img)) return img;
            string folder = Path.Combine(dir, "mapas"), path = Path.Combine(folder, Regex.Replace(file, @"[^A-Za-z0-9]+", "_") + ".png");
            if (File.Exists(path))
            {
                try { using (var fs = File.OpenRead(path)) using (var tmp = Image.FromStream(fs)) img = new Bitmap(tmp); mapCache[file] = img; return img; }
                catch { try { File.Delete(path); } catch { } }
            }
            // si fallo hace poco no se reintenta en cada repintado (antes se pedia cientos de veces por segundo)
            DateTime failedAt;
            if (mapFailed.TryGetValue(file, out failedAt) && (DateTime.Now - failedAt).TotalSeconds < 60) return null;
            if (mapPending.Add(file))
            {
                MapError = null;
                // nombre del mapa, por si hay que buscar la imagen nueva en la pagina de hens
                string mapName = cat.MapFiles.Where(kv => kv.Value.Contains(file)).Select(kv => kv.Key).FirstOrDefault();
                new Thread(() =>
                {
                    string err = null, newFile = null;
                    try
                    {
                        Directory.CreateDirectory(folder);
                        var data = Catalog.HttpBytes(HensMapUrl(file));
                        if (!IsImageData(data) && mapName != null)
                        {
                            // hens actualizo la web y la imagen cambio de nombre: releer la lista y bajar la nueva
                            var line = Catalog.HensMapList(dir, true).Select(l => l.Split('\t')).FirstOrDefault(f => f.Length >= 3 && f[0].Split('|').Contains(mapName));
                            if (line != null && line[1] != file)
                            {
                                newFile = line[1];
                                path = Path.Combine(folder, Regex.Replace(newFile, @"[^A-Za-z0-9]+", "_") + ".png");
                                data = Catalog.HttpBytes(HensMapUrl(newFile));
                            }
                        }
                        if (!IsImageData(data)) throw new Exception(Lang.L("hens333.com no devolvió una imagen", "hens333.com did not return an image"));
                        using (var ms = new MemoryStream(data))
                        {
                            var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                            System.Windows.Media.Imaging.BitmapSource src = dec.Frames[0];
                            if (src.PixelWidth > 900) src = new System.Windows.Media.Imaging.TransformedBitmap(src, new System.Windows.Media.ScaleTransform(900.0 / src.PixelWidth, 900.0 / src.PixelWidth));
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(src));
                            using (var o = File.Create(path)) enc.Save(o);
                        }
                    }
                    catch (Exception ex) { err = ex.Message; }
                    BeginInvoke((Action)(() =>
                    {
                        mapPending.Remove(file);
                        if (err != null) { mapFailed[file] = DateTime.Now; MapError = err; DebugEvent("mapa: no pude bajar " + file + " (" + err + ")"); }
                        else mapFailed.Remove(file);
                        if (newFile != null)
                        {
                            // cambiar la imagen en la lista que ya tiene la ventana del mapa (es la misma lista)
                            List<string> fl;
                            if (mapName != null && cat.MapFiles.TryGetValue(mapName, out fl)) for (int i = 0; i < fl.Count; i++) if (fl[i] == file) fl[i] = newFile;
                            if (gifMapFile == file) gifMapFile = newFile;
                        }
                        if (mapWin != null && mapWin.Visible) { mapWin.Fit(); mapWin.Invalidate(); }
                    }));
                }) { IsBackground = true }.Start();
            }
            return null;
        }

        Image GetIcon(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            Image img;
            if (iconCache.TryGetValue(file, out img)) return img;
            string folder = Path.Combine(dir, "iconos"), path = Path.Combine(folder, file);
            if (File.Exists(path))
            {
                try
                {
                    using (var fs = File.OpenRead(path))
                    using (var tmp = Image.FromStream(fs))
                        img = new Bitmap(tmp);
                    iconCache[file] = img;
                    return img;
                }
                catch { try { File.Delete(path); } catch { } }
            }
            if (iconPending.Add(file))
            {
                new Thread(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(folder);
                        var data = file.StartsWith("hens-")
                            ? Catalog.WebpToPng(Catalog.HttpBytes(Catalog.HensBase + "wiki/perks/" + file.Substring(5, file.Length - 9) + ".webp"))
                            : Catalog.HttpBytes("https://deadbydaylight.wiki.gg/images/" + Uri.EscapeDataString(file));
                        File.WriteAllBytes(path, data);
                        BeginInvoke((Action)(() => { iconPending.Remove(file); Relayout(); if (wheelWin != null && !wheelWin.IsDisposed && wheelWin.Visible) wheelWin.RefreshIcons(); }));
                    }
                    catch { } // si falla queda sin icono (no reintenta en esta sesion)
                }) { IsBackground = true }.Start();
            }
            return null;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // layered + transparente al mouse + toolwindow + topmost + no activar
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8 | 0x08000000;
                return cp;
            }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        static int Vk(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            switch (name.Trim().ToLowerInvariant())
            {
                case "alt": return (int)Keys.Menu;          // cualquiera de los dos Alt
                case "altizq": case "lalt": return (int)Keys.LMenu;
                case "altder": case "ralt": case "altgr": return (int)Keys.RMenu;
                case "ctrl": case "control": return (int)Keys.ControlKey;
                case "shift": return (int)Keys.ShiftKey;
            }
            try { return (int)(Keys)Enum.Parse(typeof(Keys), name, true); } catch { return 0; }
        }

        public Overlay(string dir)
        {
            this.dir = dir;
            cfg = Cfg.Load(Path.Combine(dir, "config.ini"));
            MigrateConfig();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // fondo transparente: la caja oscura se dibuja solo detras de los textos (los iconos quedan sueltos)
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            Opacity = SafeOp(cfg.GetD("Opacidad", 0.85));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            MakeFonts();

            string tn; ParseCombo(cfg.Get("Tecla", "Shift+V"), out vkTalk, out talkShift, out talkCtrl, out talkAlt, out tn);
            if (vkTalk == 0) vkTalk = (int)Keys.Menu;
            ParseCombo(cfg.Get("TeclaLimpiar", "Shift+G"), out vkClear, out clearSh, out clearCt, out clearAl, out tn);
            SetTypeKey(cfg.Get("TeclaEscribir", "Shift+T"));
            ParseCombo(cfg.Get("TeclaNombres", "Shift+M"), out vkNames, out namesSh, out namesCt, out namesAl, out tn);
            ParseCombo(cfg.Get("TeclaAyuda", "Shift+H"), out vkHelp, out helpSh, out helpCt, out helpAl, out tn);
            ParseCombo(cfg.Get("TeclaRueda", "Shift+R"), out vkWheel, out wheelSh, out wheelCt, out wheelAl, out tn);
            ParseCombo(cfg.Get("TeclaDeshacer", "Ctrl+Z"), out vkUndo, out undoSh, out undoCt, out undoAl, out tn);
            ParseCombo(cfg.Get("TeclaBuild", "Shift+B"), out vkBuild, out buildSh, out buildCt, out buildAl, out tn);
            { int v; ParseCombo(cfg.Get("TeclaMapaFoto", "MButton"), out v, out mapOcrSh, out mapOcrCt, out mapOcrAl, out tn); vkMapOcr = v; }
            UpdatePad();
            showNames = Regex.IsMatch(cfg.Get("Nombres", "no"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase);
            Lang.Init(cfg.Get("IdiomaApp", "auto"));
            port = cfg.GetInt("Puerto", 8765);

            try { cat = Catalog.Load(Path.Combine(dir, "datos")); }
            catch (Exception ex) { MessageBox.Show(ex.Message, AppName); Environment.Exit(1); }
            st = new State(cat);
            st.AliasPath = Path.Combine(dir, "apodos.txt");
            st.ClearKeyName = cfg.Get("TeclaLimpiar", "Shift+G");
            st.ReloadAliases();
            st.LoadFrom(Path.Combine(dir, "estado.txt"));
            // estados de versiones viejas (sin "cambio="): la hora en que se guardo el archivo
            if (st.LastChange == 0) try { var ef = new FileInfo(Path.Combine(dir, "estado.txt")); if (ef.Exists) st.LastChange = (long)(ef.LastWriteTimeUtc - new DateTime(1970, 1, 1)).TotalSeconds; } catch { }

            Debug("==== INICIO " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\r\n" + cat.Info + ", apodos: " + st.AliasCount +
                  "\r\nteclas: hablar=" + KeyName + " borrar=" + cfg.Get("TeclaLimpiar", "Shift+G") + " ayuda=" + cfg.Get("TeclaAyuda", "Shift+H") +
                  " · microfono=" + cfg.Get("Microfono", "") + "\r\nestado cargado: " + st.Describe());
            SetupTray();
            if (GifOut != null)
            {
                var gt = new System.Windows.Forms.Timer { Interval = 1500 };
                gt.Tick += (s, e) => { gt.Stop(); try { MakeGifs(); } catch (Exception ex) { File.WriteAllText(Path.Combine(GifOut, "error.txt"), ex.ToString()); } Environment.Exit(0); };
                gt.Start();
            }
            if (Lang.FirstRun && !Preview && cfg.Get("GuiaVista", "no") != "si")
            {
                var wt = new System.Windows.Forms.Timer { Interval = 800 };
                wt.Tick += (s, e) => { wt.Stop(); wt.Dispose(); ShowWelcome(); };
                wt.Start();
            }
            foreach (var si in StatusIcons) GetIcon(si.Item2); // iconos de estados: se bajan una vez
            if (!Preview) // --vista: solo dibuja (sin voz, sin sala, sin Steam) para probar el aspecto
            {
                if (VoiceOn)
                {
                    StartVoice();
                    if (ListenOn) StartListen();
                    if (HandsOn) StartHands();
                }
                else DebugEvent("voz apagada desde el menú: sin Whisper, sin micrófono");
                StartRoom();
                UpdateRoomMenu();
                // sala automatica: se mira al abrir y cada vez que DBD pasa al frente (el vigia de Steam se cierra solo con el juego)
                steamAuto = true;
                SteamTick();
            }
            else voice = true;

            // sin relojes fijos: Windows avisa cuando se aprieta una de las teclas, cuando cambia la ventana del frente
            // y cuando el cursor aparece, desaparece o se mueve. Quieta, la app no hace nada.
            var hh = Handle;
            pump = new System.Windows.Forms.Timer();
            pump.Tick += (s, e) => PumpTick();
            topLater = new System.Windows.Forms.Timer { Interval = 1500 };
            topLater.Tick += (s, e) => { topLater.Stop(); TopAll(); };
            StartKeyWatch();
            StartMouseWatch();
            StartWinEvents();
            UpdateTopGuard();
            HoverTick();
            Schedule();

            // actualizaciones de la app: solo al abrir
            if (Catalog.DataNews.Length > 0) Feedback(Catalog.DataNews, Color.FromArgb(150, 220, 150), 5000);
            if (!Preview)
            {
                Updater.CleanOld();
                CheckUpdate();
            }
            Relayout();
        }

        string KeyName { get { return cfg.Get("Tecla", "Shift+V"); } }

        // ---- voz: Whisper es opcional. Sin Whisper la app queda en "solo ver" (sirve para la sala compartida)
        bool voice, talkWarned, installing;
        string LocalApp { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        string FindWhisperExe()
        {
            foreach (var c in new[] { cfg.Get("Whisper", ""), Path.Combine(dir, "whisper", "whisper-server.exe"),
                                      Path.Combine(LocalApp, @"Programs\whisper.cpp\bin\Release\whisper-server.exe") })
                if (c.Length > 0 && File.Exists(c)) return c;
            return null;
        }

        string FindModel()
        {
            string c = cfg.Get("Modelo", "");
            if (c.Length > 0 && File.Exists(c)) return c;
            foreach (var folder in new[] { Path.Combine(dir, "whisper", "models"), Path.Combine(LocalApp, @"Programs\whisper.cpp\models") })
            {
                if (!Directory.Exists(folder)) continue;
                var bins = Directory.GetFiles(folder, "ggml-*.bin").Where(f => !Path.GetFileName(f).Contains("silero")).ToList();
                var pick = bins.FirstOrDefault(f => f.Contains("turbo")) ?? bins.FirstOrDefault(f => f.Contains("small")) ?? bins.FirstOrDefault();
                if (pick != null) return pick;
            }
            return null;
        }

        // ---- voz on/off (menu del icono): apagada no corre Whisper, ni el microfono, ni Discord, ni "sin tecla"
        bool VoiceOn { get { return !Regex.IsMatch(cfg.Get("Voz", "no"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }
        Process whisperProc;

        void SetVoice(bool on)
        {
            SetCfg("Voz", on ? "si" : "no");
            if (on)
            {
                StartVoice();
                if (voice && ListenOn) StartListen();
                if (voice && HandsOn) StartHands();
                Feedback(voice ? L("voz encendida: cargando Whisper…", "voice on: loading Whisper…") : L("voz encendida, pero esta PC no tiene Whisper (instalalo desde Configuración → Voz)", "voice on, but Whisper is not installed (Settings → Voice)"), Color.FromArgb(150, 220, 150), 5000);
            }
            else
            {
                StopHands();
                if (listening) StopListen();
                talking = false; releaseAt = -1; micRestartAt = -1;
                if (mic != null) { try { mic.Stop(); } catch { } mic = null; }
                StopWhisper();
                voice = false; serverReady = false; statusLine = "";
                DebugEvent("voz apagada: Whisper cerrado, micrófono liberado");
                Feedback(L("voz apagada: Whisper cerrado y micrófono liberado (", "voice off: Whisper closed and microphone released (") + typeKeyName + L(" para escribir)", " to type)"), Color.Silver, 5000);
            }
            Relayout();
        }

        void StopWhisper()
        {
            // solo el que arranco esta app (si ya habia uno corriendo de antes, no es nuestro)
            var p = whisperProc; whisperProc = null;
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { }
            try { p.Dispose(); } catch { }
        }

        void StartVoice()
        {
            string exe = FindWhisperExe(), model = FindModel();
            voice = exe != null && model != null;
            if (!voice) { statusLine = ""; DebugEvent("sin Whisper en esta PC: modo solo ver"); return; }
            StartWhisper(exe, model);
            StartMic();
        }

        void StartMic()
        {
            try
            {
                if (mic != null) mic.Stop();
                mic = new Mic();
                mic.Stopped = () => { try { BeginInvoke((Action)Kick); } catch { } };
                mic.Start(cfg.Get("Microfono", ""));
                if (mic.LastError.Length > 0) Feedback(L("Micrófono: ", "Microphone: ") + mic.LastError, Color.Orange, 8000);
                DebugEvent("micrófono: " + mic.DeviceName + (mic.LastError.Length > 0 ? " (" + mic.LastError + ")" : ""));
            }
            catch (Exception ex)
            {
                Feedback(L("No pude abrir el micrófono: ", "Couldn't open the microphone: ") + ex.Message, Color.OrangeRed, 15000);
                DebugEvent("micrófono: no se pudo abrir: " + ex.Message);
            }
        }

        void StartWhisper(string exe, string model)
        {
            statusLine = L("Cargando Whisper…", "Loading Whisper…");
            if (Whisper.Ping(port)) { serverReady = true; statusLine = ""; return; }
            try
            {
                int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2));
                var psi = new ProcessStartInfo(exe, "-m \"" + model + "\" --host 127.0.0.1 --port " + port + " -nt -t " + threads)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
                var p = Process.Start(psi);
                Job.Add(p);
                whisperProc = p;
                DebugEvent("Whisper arrancando: " + exe + " · modelo " + Path.GetFileName(model) + " · " + threads + " hilos");
            }
            catch (Exception ex) { statusLine = L("Whisper no arrancó: ", "Whisper didn't start: ") + ex.Message; return; }
            new Thread(() =>
            {
                for (int i = 0; i < 180 && !Whisper.Ping(port); i++) Thread.Sleep(500);
                bool ok = Whisper.Ping(port);
                BeginInvoke((Action)(() =>
                {
                    serverReady = ok;
                    statusLine = ok ? "" : L("Whisper no responde", "Whisper is not responding");
                    DebugEvent(ok ? "Whisper listo" : "Whisper NO responde en el puerto " + port);
                    Relayout();
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- instalar voz: baja Whisper (GPU NVIDIA o procesador) y un modelo, a la carpeta whisper\ del programa
        static bool HasNvidia()
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (var o in s.Get())
                        if ((o["Name"] ?? "").ToString().IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return File.Exists(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"));
        }

        void InstallVoice()
        {
            if (installing) return;
            if (voice) { MessageBox.Show(L("Esta PC ya tiene Whisper: la voz ya funciona (mantené ", "Whisper is already installed: voice works (hold ") + KeyName + L(" y hablá).", " and talk)."), AppName); return; }
            bool nv = HasNvidia();
            const string rel = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/";
            string zipUrl = rel + (nv ? "whisper-bin-win-cuda-11.8.0-x64.zip" : "whisper-bin-x64.zip");
            string modelName = nv ? "ggml-large-v3-turbo-q5_0.bin" : "ggml-small-q5_1.bin";
            string modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + modelName;
            string wdir = Path.Combine(dir, "whisper"), mdir = Path.Combine(wdir, "models");
            string msg = Lang.En
                ? (nv ? "NVIDIA graphics card found: I'll download Whisper with GPU acceleration (fast)."
                      : "No NVIDIA graphics card found: I'll download Whisper for the CPU (a bit slower to understand).") +
                  "\n\nDownload: " + (nv ? "about 860 MB" : "about 200 MB") + " (from GitHub and Hugging Face).\nSaved in " + wdir + "\n\nContinue?"
                : (nv ? "Detecté una placa NVIDIA: voy a bajar Whisper con aceleración por GPU (rápido)."
                      : "No detecté placa NVIDIA: voy a bajar Whisper para el procesador (tarda un poco más en entender).") +
                  "\n\nDescarga: " + (nv ? "unos 860 MB" : "unos 200 MB") + " (desde GitHub y Hugging Face).\nSe guarda en " + wdir + "\n\n¿Seguir?";
            if (MessageBox.Show(msg, L("Instalar voz", "Install voice"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            installing = true;
            DebugEvent("instalando voz (" + (nv ? "NVIDIA" : "procesador") + ")");
            new Thread(() =>
            {
                string err = null;
                try
                {
                    Directory.CreateDirectory(mdir);
                    string zip = Path.Combine(wdir, "descarga.zip");
                    Download(zipUrl, zip, "Whisper");
                    Status(L("Descomprimiendo Whisper…", "Unpacking Whisper…"));
                    using (var z = System.IO.Compression.ZipFile.OpenRead(zip))
                        foreach (var en in z.Entries)
                        {
                            if (string.IsNullOrEmpty(en.Name) || !Regex.IsMatch(en.Name, @"\.(exe|dll)$", RegexOptions.IgnoreCase)) continue;
                            System.IO.Compression.ZipFileExtensions.ExtractToFile(en, Path.Combine(wdir, en.Name), true); // sin la carpeta Release/
                        }
                    File.Delete(zip);
                    string model = Path.Combine(mdir, modelName);
                    if (!File.Exists(model)) Download(modelUrl, model, L("modelo de voz", "voice model"));
                }
                catch (Exception ex) { err = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    installing = false; statusLine = "";
                    if (err != null)
                    {
                        Feedback(L("No se pudo instalar Whisper: ", "Couldn't install Whisper: ") + err, Color.OrangeRed, 15000);
                        DebugEvent("instalar voz falló: " + err);
                        Relayout();
                        return;
                    }
                    DebugEvent("voz instalada");
                    Feedback(L("¡Voz instalada! Mantené ", "Voice installed! Hold ") + KeyName + L(" y hablá", " and talk"), Color.FromArgb(150, 220, 150), 8000);
                    StartVoice();
                    Relayout();
                }));
            }) { IsBackground = true }.Start();
        }

        void Status(string s) { BeginInvoke((Action)(() => { statusLine = s; Relayout(); })); }

        void Download(string url, string dest, string what)
        {
            string part = dest + ".part";
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "DbdOverlay/1.0"; req.Timeout = 30000; req.ReadWriteTimeout = 60000;
            using (var resp = req.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var fs = File.Create(part))
            {
                long len = resp.ContentLength, got = 0;
                var b = new byte[1 << 16];
                int n, lastPct = -1;
                while ((n = rs.Read(b, 0, b.Length)) > 0)
                {
                    fs.Write(b, 0, n); got += n;
                    int pct = len > 0 ? (int)(got * 100 / len) : (int)(got >> 20);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        Status(L("Descargando ", "Downloading ") + what + "… " + (len > 0 ? pct + "%" : pct + " MB"));
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }

        // ---- idioma de los nombres y de las descripciones (por separado). Por voz se entienden los dos siempre.
        bool NamesEs { get { return cfg.Get("IdiomaNombres", "en").ToLowerInvariant().StartsWith("es"); } }
        bool DescEs { get { return !cfg.Get("IdiomaDescripciones", Lang.En ? "en" : "es").ToLowerInvariant().StartsWith("en"); } }

        // ---- nombre del killer arriba: se puede ocultar y cambiar de tamano
        bool KillerOn { get { return !Regex.IsMatch(cfg.Get("MostrarKiller", "si"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }
        Font killerFont;
        Font KillerFont()
        {
            float sz = Math.Max(5f, cfg.GetInt("TamanoKiller", 12) * UiScale);
            if (killerFont == null || Math.Abs(killerFont.Size - sz) > 0.01f) killerFont = new Font("Segoe UI", sz, FontStyle.Bold);
            return killerFont;
        }

        ToolStripMenuItem KillerMenu()
        {
            var m = new ToolStripMenuItem("Nombre del killer");
            var show = new ToolStripMenuItem("Mostrar arriba");
            show.Click += (s, e) => { SetCfg("MostrarKiller", KillerOn ? "no" : "si"); Relayout(); };
            m.DropDownItems.Add(show);
            m.DropDownItems.Add(new ToolStripSeparator());
            foreach (var sz in new[] { Tuple.Create("Mínimo", 7), Tuple.Create("Chico", 8), Tuple.Create("Normal", 10), Tuple.Create("Grande", 12), Tuple.Create("Muy grande", 15) })
            {
                var it = new ToolStripMenuItem(sz.Item1) { Tag = sz.Item2 };
                it.Click += (s, e) => { SetCfg("TamanoKiller", ((int)((ToolStripMenuItem)s).Tag).ToString()); if (!KillerOn) SetCfg("MostrarKiller", "si"); Relayout(); };
                m.DropDownItems.Add(it);
            }
            m.DropDownOpening += (s, e) =>
            {
                show.Checked = KillerOn;
                int cur = cfg.GetInt("TamanoKiller", 12);
                foreach (var o in m.DropDownItems.OfType<ToolStripMenuItem>()) if (o.Tag is int) o.Checked = (int)o.Tag == cur;
            };
            return m;
        }

        string DName(Entry e)
        {
            if (e.Synth) return L(e.NameEs, e.Name); // no es del juego: va en el idioma de la app
            if (NamesEs) return string.IsNullOrEmpty(e.NameEs) ? e.Name : e.NameEs;
            return e.Rarity == "secreta" ? "Secret Offering" : e.Name;
        }

        string DInfo(Entry e)
        {
            if (DescEs) return string.IsNullOrEmpty(e.Info) ? e.InfoEn : e.Info;
            return string.IsNullOrEmpty(e.InfoEn) ? e.Info : e.InfoEn;
        }

        ToolStripMenuItem LangMenu(string title, string key, string def)
        {
            var m = new ToolStripMenuItem(title);
            foreach (var opt in new[] { Tuple.Create("en", "Inglés"), Tuple.Create("es", "Español") })
            {
                var it = new ToolStripMenuItem(opt.Item2) { Tag = opt.Item1 };
                it.Click += (s, e) =>
                {
                    SetCfg(key, (string)((ToolStripMenuItem)s).Tag);
                    if (tip != null) { tip.Hide(); hoverE = null; }
                    Relayout();
                    Feedback(title + ": " + ((ToolStripMenuItem)s).Text, Color.Silver, 3000);
                };
                m.DropDownItems.Add(it);
            }
            m.DropDownOpening += (s, e) =>
            {
                string cur = cfg.Get(key, def).ToLowerInvariant().StartsWith("es") ? "es" : "en";
                foreach (ToolStripMenuItem o in m.DropDownItems) o.Checked = (string)o.Tag == cur;
            };
            return m;
        }

        // ---- hover: descripcion del perk/addon/ofrenda bajo el cursor
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public int x, y; }

        HoverTip tip;
        Entry hoverE;
        // ---- clics en la lista (con el cursor libre): doble clic confirma una sospecha; mantener apretado la quita.
        // La ventana deja pasar el mouse siempre, salvo con el cursor encima de algo anotado.
        bool ListClicks { get { return cfg.Get("ListaClics", "si") != "no"; } }
        Entry holdE; bool holdShown, holdGhost; int holdT1; float holdP;
        const int HoldMs = 600;
        // solo se repinta el icono que se esta apretando
        // la parte de un poligono que queda por debajo de la altura y
        static PointF[] BelowLine(PointF[] poly, float y)
        {
            var o = new List<PointF>();
            for (int i = 0; i < poly.Length; i++)
            {
                PointF a = poly[i], b = poly[(i + 1) % poly.Length];
                bool ina = a.Y >= y, inb = b.Y >= y;
                if (ina) o.Add(a);
                if (ina != inb) o.Add(new PointF(a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y), y));
            }
            return o.ToArray();
        }
        void InvalidateHold() { foreach (var it in items) if (it.E == holdE) { Invalidate(Rectangle.Inflate(it.R, 2, 2)); return; } Invalidate(); }
        System.Windows.Forms.Timer holdTimer; // solo corre mientras se mantiene apretado (0,6 s)
        bool listClickable;
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int LwGet(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int LwSet(IntPtr h, int i, int v);
        Entry ListEntryAt(Point p)
        {
            var cur = st.Current();
            foreach (var it in items) if (it.E != null && cur.Contains(it.E) && Hit(it, p)) return it.E;
            return null;
        }
        // los perks son rombos y sus cuadrados se pisan entre si: se mira la forma real
        static bool Hit(Item it, Point p)
        {
            if (!it.R.Contains(p)) return false;
            if (!it.Slot) return true;
            float dx = Math.Abs(p.X - (it.R.X + it.R.Width / 2f)) / (it.R.Width / 2f), dy = Math.Abs(p.Y - (it.R.Y + it.R.Height / 2f)) / (it.R.Height / 2f);
            return dx + dy <= 1f;
        }
        void SetListClickable(bool on)
        {
            if (on == listClickable) return;
            listClickable = on;
            int ex = LwGet(Handle, -20);
            LwSet(Handle, -20, on ? ex & ~0x20 : ex | 0x20);
        }
        void CancelHold() { if (holdTimer != null) holdTimer.Stop(); if (holdE != null) { bool was = holdShown; holdE = null; holdShown = false; if (was) Invalidate(); } }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !ListClicks) return;
            if (RotateAt(e.Location)) { NextBuild(); return; }
            var en = ListEntryAt(e.Location);
            bool ghost = false;
            if (en == null) { en = GhostAt(e.Location); ghost = en != null; }
            if (en == null) return;
            holdE = en; holdGhost = ghost; holdShown = false;
            if (holdTimer == null)
            {
                holdTimer = new System.Windows.Forms.Timer();
                holdTimer.Tick += (s, a) =>
                {
                    // primero (0,25 s) se marca en rojo; despues (0,6 s mas) se quita
                    // despues, se llena de rojo de abajo hacia arriba (~30 cuadros por segundo, solo ese icono) y al llenarse se quita
                    if (!holdShown) { holdShown = true; holdT1 = Environment.TickCount; holdP = 0; holdTimer.Interval = 33; InvalidateHold(); return; }
                    holdP = Math.Min(1f, (Environment.TickCount - holdT1) / (float)HoldMs);
                    if (holdP < 1f) { InvalidateHold(); return; }
                    holdTimer.Stop(); holdShown = false;
                    var x = holdE; holdE = null;
                    if (x != null && holdGhost) { ghostOff.Add(x); Relayout(); }  // sugerido: deja de sugerirse en esta partida
                    else if (x != null) { ghostOff.Add(x); BoxDo(L("quitar ", "remove ") + x.Name, () => st.RemovePicked(x), "lista"); } // quitado a proposito: tampoco vuelve como sugerido
                    Invalidate();
                };
            }
            holdTimer.Stop(); holdTimer.Interval = 250; holdTimer.Start();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); CancelHold(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); CancelHold(); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            CancelHold();
            if (e.Button != MouseButtons.Left || !ListClicks) return;
            var en = ListEntryAt(e.Location);
            if (en != null && st.IsDoubt(en)) BoxDo(L("confirmar ", "confirm ") + en.Name, () => st.ToggleDoubt(en), "lista");
            var gh = en == null ? GhostAt(e.Location) : null;
            if (gh != null) BoxDo(gh.Name, () => st.AddPicked(gh, false), "lista");
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE: el clic llega, el foco queda en el juego
            base.WndProc(ref m);
        }
        int hoverSince;

        // ---- visibilidad: "siempre" o "solo con el menu de DBD abierto".
        // No se lee nada del juego: con DBD en primer plano, el menu (ESC) es cuando el cursor esta visible.
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        bool MenuOnly { get { return cfg.Get("Visibilidad", "siempre").ToLowerInvariant().StartsWith("menu"); } }
        int fgPid = -1; bool fgIsDbd;
        bool hiddenByMenu;

        bool DbdInFront()
        {
            int pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            if (pid != fgPid)
            {
                fgPid = pid; fgIsDbd = false;
                try { fgIsDbd = Process.GetProcessById(pid).ProcessName.StartsWith("DeadByDaylight", StringComparison.OrdinalIgnoreCase); } catch { }
            }
            return fgIsDbd;
        }

        // mapa "solo con menu ESC": jugando se oculta (mientras escribis en el cuadro se ve)
        bool MapMenuOnly { get { return cfg.Get("MapaVisibilidad", "siempre").ToLowerInvariant().StartsWith("menu"); } }
        void UpdateMapVisibility(bool cursor)
        {
            if (mapWin == null) return;
            bool ok = mapShown != null && !(MapMenuOnly && DbdInFront() && !cursor && !(typeBox != null && typeBox.Visible));
            if (ok && !mapWin.Visible) { mapWin.Show(); SetWindowPos(mapWin.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); }
            else if (!ok && mapWin.Visible) mapWin.Hide();
            if (mapWin.Visible) mapWin.SetClickable(MapMarksOn && (cursor || (typeBox != null && typeBox.Visible)));
        }

        void UpdateVisibility(bool cursor)
        {
            UpdateMapVisibility(cursor);
            bool show = true;
            if (MenuOnly && DbdInFront() && !cursor)
                // jugando: oculto, salvo mientras hablás o hay un mensaje / ayuda / candidatos para elegir
                show = talking || busy || releaseAt >= 0 || helpShown || feedback.Length > 0 || st.Pick != null;
            if (show == !hiddenByMenu) return;
            hiddenByMenu = !show;
            if (show) { Show(); SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
            else { Hide(); if (tip != null) tip.Hide(); }
        }

        void HoverTick()
        {
            // los candidatos de addon se van solos a los 30 s (queda anotado el primero como sospecha)
            if (st.Pick != null && Environment.TickCount - st.PickUntil > 0) { st.Pick = null; Relayout(); }
            Entry over = null; Rectangle rr = Rectangle.Empty;
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            // en partida DBD esconde el cursor: ahi no se muestra nada (algunos juegos lo "esconden" dejandolo sin imagen)
            bool cursor = GetCursorInfo(ref ci) && (ci.flags & 1) != 0 && ci.hCursor != IntPtr.Zero;
            cursorShown = cursor;
            // se cerro el menu (ESC) y volviste a jugar: la rueda se va
            if (!cursor && wheelWin != null && !wheelWin.IsDisposed && wheelWin.Visible && !wheelWin.Gesture && DbdInFront()) wheelWin.Hide();
            UpdateVisibility(cursor);
            SetMoveHook(cursor && Visible);
            bool ro = Visible && cursor && roomRect.Contains(new Point(ci.x - Left, ci.y - Top));
            if (ro != roomHover) { roomHover = ro; Relayout(); }
            if (Visible && !helpShown && cursor)
            {
                var p = new Point(ci.x - Left, ci.y - Top);
                foreach (var it in items)
                    if (it.E != null && !string.IsNullOrEmpty(DInfo(it.E)) && Hit(it, p)) { over = it.E; rr = it.R; }
            }
            SetListClickable(ListClicks && Visible && cursor && (holdE != null || ListEntryAt(new Point(ci.x - Left, ci.y - Top)) != null || GhostAt(new Point(ci.x - Left, ci.y - Top)) != null || RotateAt(new Point(ci.x - Left, ci.y - Top))));
            bool rh = ListClicks && Visible && cursor && RotateAt(new Point(ci.x - Left, ci.y - Top));
            if (rh != rotHover) { rotHover = rh; Invalidate(); } // el boton bajo el cursor se aclara (se puede clickear)
            if (over != hoverE) { hoverE = over; hoverSince = Environment.TickCount; if (over == null && tip != null) tip.Hide(); return; }
            if (over == null || Environment.TickCount - hoverSince < 250) return;
            if (tip != null && tip.Visible && tip.Current == over) return;
            if (tip == null) tip = new HoverTip(new Font("Segoe UI", fItem.Size, FontStyle.Bold), fSmall, GetIcon);
            string title = DName(over);
            string sub = over.K == Kind.Perk ? (Lang.En ? (!string.IsNullOrEmpty(over.Owner) ? over.Owner + " perk" : "General perk") : "Perk" + (!string.IsNullOrEmpty(over.Owner) ? " de " + over.Owner : " general"))
                       : over.K == Kind.Offering ? L("Ofrenda", "Offering") : L("Addon", "Add-on") + (over.Killer != null && over.Killer.Length > 0 ? " · " + over.Killer : "");
            if (st.IsDoubt(over)) sub += L(" · sospecha (sin confirmar)", " · guess (not confirmed)");
            Color tc = over.K == Kind.Perk ? Color.WhiteSmoke : RarityColor(over.Rarity);
            var anchor = new Rectangle(Left + rr.X, Top + rr.Y, rr.Width, rr.Height);
            bool right = !cfg.Get("Posicion", "arriba-derecha").ToLowerInvariant().Contains("izq");
            tip.ShowFor(over, title, tc, sub, DInfo(over), anchor, Bounds, right);
        }

        // estados del juego -> icono de la wiki (se marcan en las descripciones con su icono)
        static readonly Tuple<Regex, string>[] StatusIcons = {
            Tuple.Create(new Regex(@"\b(?:Herida profunda|Deep Wound)\b"), "IconStatusEffects_deepWound.png"),
            Tuple.Create(new Regex(@"\b(?:Hemorragia|Hemorrhage|Haemorrhage)\b"), "IconStatusEffects_bleeding.png"),
            Tuple.Create(new Regex(@"\b(?:Laceración|Mutilad[oa]s?|Mutilación|Mangled)\b"), "IconStatusEffects_mangled.png"),
            Tuple.Create(new Regex(@"\b(?:Expuest[oa]s?|Exposed)\b"), "IconStatusEffects_exposed.png"),
            Tuple.Create(new Regex(@"\b(?:Celeridad|Prisa|Haste)\b"), "IconStatusEffects_haste.png"),
            Tuple.Create(new Regex(@"\b(?:Obstaculizad[oa]s?|Obstaculización|Hindered)\b"), "IconStatusEffects_hindered.png"),
            Tuple.Create(new Regex(@"\b(?:Ceguera|Cegad[oa]s?|Blindness|Blinded)\b"), "IconStatusEffects_vision.png"),
            Tuple.Create(new Regex(@"\b(?:Quebrantad[oa]s?|Destrozad[oa]s?|Herida grave|Broken)\b"), "IconStatusEffects_broken.png"),
            Tuple.Create(new Regex(@"\b(?:Agotamiento|Agotad[oa]s?|Exhausted|Exhaustion)\b"), "IconStatusEffects_exhausted.png"),
            Tuple.Create(new Regex(@"\b(?:Inconsciencia|Inconscientes?|Oblivious)\b"), "IconStatusEffects_oblivious.png"),
            Tuple.Create(new Regex(@"\b(?:Indetectable|Undetectable)\b"), "IconStatusEffects_undetectable.png"),
            Tuple.Create(new Regex(@"\b(?:Incapacitad[oa]s?|Incapacitación|Incapacitated)\b"), "IconStatusEffects_incapacitated.png"),
            Tuple.Create(new Regex(@"\b(?:Locura|Madness)\b"), "IconStatusEffects_madness.png"),
            Tuple.Create(new Regex(@"\b(?:Resistencia|Endurance)\b"), "IconStatusEffects_enduranceSurvivor.png"),
            Tuple.Create(new Regex(@"\b(?:Esquiv[oa]|Elusive)\b"), "IconStatusEffects_elusive.png"),
            Tuple.Create(new Regex(@"\b(?:Maldici[oó]n|Maldit[oa]s?|Cursed)\b"), "IconStatusEffects_cursed.png"),
        };

        class HoverTip : Form
        {
            public Entry Current;
            string title = "", sub = "", body = "";
            Color tc;
            readonly Font ft, fb, fs, fk;
            readonly Func<string, Image> icon;
            const int Pad = 10, MaxW = 400;
            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            const TextFormatFlags One = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            static readonly Color StatusColor = Color.FromArgb(240, 200, 110);

            class Op { public string T; public Image Img; public Rectangle R; public Color C; public bool Bold; }
            List<Op> ops = new List<Op>();

            public HoverTip(Font title, Font body, Func<string, Image> getIcon)
            {
                ft = title; fb = body; fs = new Font(body.FontFamily, body.Size * 0.9f, FontStyle.Italic); fk = new Font(body.FontFamily, body.Size, FontStyle.Bold);
                icon = getIcon;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(18, 18, 24); TopMost = true; DoubleBuffered = true; Opacity = 0.96;
            }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80 | 0x8 | 0x08000000; return cp; } // transparente al mouse, sin activar
            }
            protected override bool ShowWithoutActivation { get { return true; } }

            static int W(string s, Font f) { return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), One).Width; }

            // arma el texto palabra por palabra: los estados van con su icono y resaltados
            // unidades cortas: "32 metros" -> "32m", "30 segundos" -> "30s", "4 %" -> "4%"
            static readonly Regex UnitRx = new Regex(@"(\d+(?:[.,]\d+)?)\s*(?:(metros?|meters?|metres?)|(segundos?|seconds?)|(%|por ciento|percent))(?![\p{L}])", RegexOptions.IgnoreCase);
            static readonly Regex NumTokRx = new Regex(@"^[+\-−]?\d");
            static readonly Color NumColor = Color.FromArgb(120, 200, 255), BulletColor = Color.FromArgb(150, 150, 170), TriggerColor = Color.White;

            static string Shorten(string s)
            {
                return UnitRx.Replace(s, m => m.Groups[1].Value + (m.Groups[2].Success ? "m" : m.Groups[3].Success ? "s" : "%"));
            }

            Size LayoutBody(int maxW)
            {
                ops = new List<Op>();
                int lh = Math.Max(fb.Height, fk.Height) + 1, isz = fb.Height, space = W("a a", fb) - W("aa", fb);
                // TextRenderer suma un margen a cada medida: se descuenta para que las palabras queden a distancia normal
                int over = Math.Max(0, 2 * W("a", fb) - W("aa", fb)), overK = Math.Max(0, 2 * W("a", fk) - W("aa", fk));
                int x = 0, y = 0, wmax = 0;
                bool first = true;
                foreach (var raw in Shorten(body).Split('\n'))
                {
                    string para = raw.Trim();
                    if (para.Length == 0) continue;
                    if (!first) y += lh / 3; // aire entre parrafos y vinetas
                    first = false;
                    // vinetas: punto de color y las lineas siguientes van sangradas
                    int indent = 0;
                    bool bullet = para.StartsWith("•");
                    if (bullet)
                    {
                        para = para.TrimStart('•', ' ');
                        ops.Add(new Op { T = "•", R = new Rectangle(0, y, W("•", fk) + 2, lh), C = BulletColor, Bold = true });
                        indent = W("• ", fk) - overK;
                    }
                    x = indent;
                    // la condicion ("Cuando...:", "While...:") va mas brillante para separarla de los efectos
                    Color baseC = !bullet && para.EndsWith(":") ? TriggerColor : Color.Gainsboro;
                    // trozos: texto comun y estados
                    var parts = new List<Tuple<string, string>>(); // (texto, archivo de icono o null)
                    int pos = 0;
                    while (pos < para.Length)
                    {
                        Match best = null; string file = null;
                        foreach (var si in StatusIcons)
                        {
                            var m = si.Item1.Match(para, pos);
                            if (m.Success && (best == null || m.Index < best.Index)) { best = m; file = si.Item2; }
                        }
                        if (best == null) { parts.Add(Tuple.Create(para.Substring(pos), (string)null)); break; }
                        if (best.Index > pos) parts.Add(Tuple.Create(para.Substring(pos, best.Index - pos), (string)null));
                        parts.Add(Tuple.Create(best.Value, file));
                        pos = best.Index + best.Length;
                    }
                    foreach (var p in parts)
                    {
                        if (p.Item2 != null)
                        {
                            var img = icon(p.Item2);
                            int ww = (img != null ? isz + 2 : 0) + W(p.Item1, fk) - overK;
                            if (x > indent && x + ww > maxW) { x = indent; y += lh; }
                            if (img != null) { ops.Add(new Op { Img = img, R = new Rectangle(x, y + (lh - isz) / 2 - 1, isz, isz) }); x += isz + 2; }
                            int tw = W(p.Item1, fk) - overK;
                            ops.Add(new Op { T = p.Item1, R = new Rectangle(x, y, tw + 2, lh), C = StatusColor, Bold = true });
                            x += tw; wmax = Math.Max(wmax, x);
                            continue;
                        }
                        // texto comun: palabra por palabra; los numeros resaltados
                        foreach (var tok in Regex.Split(p.Item1, @"(\s+|(?<=\d[%sm]?)(?=[.,;:)]+(?:\s|$)))"))
                        {
                            if (tok.Length == 0) continue;
                            if (tok.Trim().Length == 0) { if (x > indent) x += space; continue; }
                            bool num = NumTokRx.IsMatch(tok);
                            var f = num ? fk : fb;
                            int tw = W(tok, f) - (num ? overK : over);
                            if (x > indent && x + tw > maxW) { x = indent; y += lh; }
                            ops.Add(new Op { T = tok, R = new Rectangle(x, y, tw + 2, lh), C = num ? NumColor : baseC, Bold = num });
                            x += tw; wmax = Math.Max(wmax, x);
                        }
                    }
                    y += lh;
                }
                return new Size(wmax, y);
            }

            public void ShowFor(Entry e, string t, Color c, string s, string b, Rectangle anchor, Rectangle overlay, bool leftOfOverlay)
            {
                Current = e; title = t; tc = c; sub = s; body = b ?? "";
                var big = new Size(MaxW, 4000);
                Size st = TextRenderer.MeasureText(title, ft, big, Wrap), ss = TextRenderer.MeasureText(sub, fs, big, Wrap), sb = LayoutBody(MaxW);
                int w = Math.Max(st.Width, Math.Max(ss.Width, sb.Width)) + Pad * 2 + 4;
                int h = st.Height + ss.Height + sb.Height + Pad * 2 + 10;
                var scr = Screen.FromRectangle(overlay).WorkingArea;
                int x = leftOfOverlay ? overlay.Left - w - 8 : overlay.Right + 8;
                int y = Math.Max(scr.Top, Math.Min(anchor.Top, scr.Bottom - h));
                x = Math.Max(scr.Left, Math.Min(x, scr.Right - w));
                Bounds = new Rectangle(x, y, w, h);
                if (!Visible) Show();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                using (var pen = new Pen(Color.FromArgb(Math.Min(255, tc.R / 2 + 40), Math.Min(255, tc.G / 2 + 40), Math.Min(255, tc.B / 2 + 40)), 2))
                    g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                int y = Pad, w = Width - Pad * 2;
                var big = new Size(w, 4000);
                var r = new Rectangle(Pad, y, w, TextRenderer.MeasureText(title, ft, big, Wrap).Height);
                TextRenderer.DrawText(g, title, ft, r, tc, Wrap); y = r.Bottom + 2;
                r = new Rectangle(Pad, y, w, TextRenderer.MeasureText(sub, fs, big, Wrap).Height);
                TextRenderer.DrawText(g, sub, fs, r, Color.FromArgb(150, 150, 165), Wrap); y = r.Bottom + 8;
                foreach (var op in ops)
                {
                    var rr = op.R; rr.Offset(Pad, y);
                    if (op.Img != null) g.DrawImage(op.Img, rr);
                    else TextRenderer.DrawText(g, op.T, op.Bold ? fk : fb, rr, op.C, One);
                }
            }
        }

        // ---- actualizaciones: si hay version nueva firmada, la instala y reinicia (la lista se conserva)
        bool updating;

        void CheckUpdate() { CheckUpdate(false); }
        void CheckUpdate(bool manual)
        {
            if (updating) return;
            updating = true;
            new Thread(() =>
            {
                string err, what = Updater.CheckAndInstall(out err);
                BeginInvoke((Action)(() =>
                {
                    updating = false;
                    if (err != null) DebugEvent("actualización: " + err);
                    if (manual && what == null) Feedback(err != null ? L("No pude buscar la actualización (", "Couldn't check for updates (") + err + ")" : L("Ya tenés la última versión (", "You have the latest version (") + Updater.AppVersion + ")", err != null ? Color.FromArgb(240, 170, 80) : Color.FromArgb(150, 220, 150), 5000);
                    if (what == null) return;
                    DebugEvent("actualización instalada (" + what + "): reinicio");
                    Feedback(L("Callouts in the Fog actualizado a la ", "Callouts in the Fog updated to ") + what + L(" · reiniciando…", " · restarting…"), Color.FromArgb(150, 220, 150), 4000);
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        restartingForUpdate = true; // se reinicia sola: sigue en la sala
                        try { Process.Start(Application.ExecutablePath, "--reinicio " + Process.GetCurrentProcess().Id); } catch { }
                        Close();
                    };
                    t.Start();
                }));
            }) { IsBackground = true }.Start();
        }

        // ---- escuchar Discord (prueba): transcribe lo que suena en la PC y anota como sospecha lo que dicen los amigos
        Loopback loop;
        volatile bool listening;
        ToolStripMenuItem listenItem;
        static readonly Regex HeardRx = new Regex(@"\b(tiene|trajo|quemo|ofrenda|tenia)\b.*$");
        bool ListenOn { get { return Regex.IsMatch(cfg.Get("EscucharDiscord", "si"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase); } }

        void SetListen(bool v)
        {
            SetCfg("EscucharDiscord", v ? "si" : "no");
            if (listenItem != null) listenItem.Checked = v;
            if (v) StartListen(); else StopListen();
            Feedback(v ? L("escuchando Discord: lo que digan con \"tiene…\" se anota como sospecha", "listening to Discord: what they say with \"he has…\" is added as a guess") : L("ya no escucho Discord", "stopped listening to Discord"), Color.FromArgb(130, 190, 255), 5000);
        }

        void StopListen()
        {
            listening = false;
            if (loop != null) { loop.Stop(); loop = null; }
            DebugEvent("discord: dejé de escuchar");
        }

        void StartListen()
        {
            if (listening) return;
            if (!voice) { Feedback(L("para escuchar Discord hace falta Whisper", "listening to Discord needs Whisper"), Color.Orange, 5000); return; }
            var lb = new Loopback();
            lb.Start();
            loop = lb; listening = true;
            DebugEvent("discord: escuchando la salida de audio de la PC");
            new Thread(() =>
            {
                Thread.Sleep(800);
                if (lb.LastError.Length > 0) { BeginInvoke((Action)(() => { DebugEvent("discord: ERROR " + lb.LastError); Feedback(L("no pude escuchar el audio de la PC: ", "couldn't capture the PC audio: ") + lb.LastError, Color.OrangeRed, 8000); })); return; }
                RunVad(() => lb.Pos, lb.Get, 500, 0, () => listening && loop == lb, () => false, OnHeard);
            }) { IsBackground = true }.Start();
        }

        // corta el audio en frases (voz entre silencios) y pasa cada una por Whisper.
        // thr: volumen minimo de voz; peak: volumen pico minimo de la frase (0 = sin minimo); paused: no escuchar (ej. mientras se aprieta la tecla)
        void RunVad(Func<long> posF, Func<long, long, short[]> getF, int thr, int peak, Func<bool> alive, Func<bool> paused, Action<string, double, int> heard)
        {
            long pos = posF(), segStart = -1; int quiet = 0; double segPeak = 0;
            const int frame = 320; // 20 ms
            while (alive())
            {
                Thread.Sleep(100);
                long end = posF();
                if (end < pos || paused()) { pos = end; segStart = -1; continue; } // micro reiniciado o tecla apretada
                while (pos + frame <= end && alive())
                {
                    var f = getF(pos, pos + frame);
                    double e = 0; foreach (var x in f) e += (double)x * x;
                    double rms = Math.Sqrt(e / Math.Max(1, f.Length));
                    bool loud = rms > thr;
                    if (segStart < 0) { if (loud) { segStart = Math.Max(0, pos - 16000 * 3 / 10); quiet = 0; segPeak = rms; } }
                    else
                    {
                        quiet = loud ? 0 : quiet + 1;
                        segPeak = Math.Max(segPeak, rms);
                        if (quiet >= 30 || pos - segStart >= 16000 * 8) // 0,6 s de silencio o 8 s maximo
                        {
                            var seg = getF(segStart, pos);
                            segStart = -1;
                            if (seg.Length >= 16000 * 6 / 10 && segPeak >= peak && serverReady && !paused())
                            {
                                string text = null, prompt = null;
                                try { Invoke((Action)(() => prompt = st.Prompt())); } catch { return; }
                                var t0 = DateTime.Now;
                                try { text = Whisper.Transcribe(port, Whisper.Wav(seg), prompt); } catch { }
                                int ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                                double secs = seg.Length / 16000.0;
                                if (text != null) { string tx = text; BeginInvoke((Action)(() => heard(tx, secs, ms))); }
                            }
                        }
                    }
                    pos += frame;
                }
            }
        }

        // ---- hablar sin tecla (prueba): escucha el micrófono todo el tiempo y solo anota frases claras de comando
        volatile bool handsFree;
        int handsGen;
        ToolStripMenuItem handsItem;
        // "tiene X", "creo que tiene X", "trajo/quemó X", "ofrenda X", "el killer es X"
        static readonly Regex HandsRx = new Regex(@"\b(?:(?:creo|me parece|probablemente|capaz)\s+(?:que\s+)?)?(?:tiene|tenia|trajo|quemo|ofrenda|(?:el\s+)?(?:(?:k|qu)i(?:ll?|r)er|asesino)\s+es)\b.*$");
        bool HandsOn { get { return Regex.IsMatch(cfg.Get("SinTecla", "no"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase); } }

        void SetHands(bool v)
        {
            SetCfg("SinTecla", v ? "si" : "no");
            if (handsItem != null) handsItem.Checked = v;
            if (v) StartHands(); else StopHands();
            Feedback(v ? L("hablar sin tecla: decí \"tiene…\", \"creo que tiene…\", \"el killer es…\" (sí/no/quitar siguen con ", "talk without a key: say \"he has…\", \"I think he has…\", \"the killer is…\" (yes/no/remove still use ") + KeyName + ")" : L("hablar sin tecla: apagado", "talk without a key: off"), Color.FromArgb(150, 220, 150), 6000);
        }

        void StopHands()
        {
            if (!handsFree) return;
            handsFree = false; handsGen++;
            DebugEvent("sin tecla: apagado");
        }

        void StartHands()
        {
            if (handsFree) return;
            if (!voice) { Feedback(L("para hablar sin tecla hace falta Whisper", "talking without a key needs Whisper"), Color.Orange, 5000); return; }
            handsFree = true;
            int gen = ++handsGen;
            DebugEvent("sin tecla: escuchando el micrófono");
            new Thread(() =>
            {
                while (mic == null && handsFree && gen == handsGen) Thread.Sleep(500);
                RunVad(() => { var m = mic; return m != null ? m.Pos : 0; },
                       (a, b) => { var m = mic; return m != null ? m.Get(a, b) : new short[0]; },
                       300, 700, () => handsFree && gen == handsGen, () => talking || releaseAt >= 0 || busy, OnHandsHeard);
            }) { IsBackground = true }.Start();
        }

        // ---- teclas configurables: cada accion es una combinacion ("Shift+V", "Ctrl+Alt+F5", "XButton1" = boton lateral del mouse)
        bool clearSh, clearCt, clearAl, helpSh, helpCt, helpAl, namesSh, namesCt, namesAl;
        KeyCapture keyCapture;

        void LoadKeys()
        {
            string n;
            ParseCombo(cfg.Get("Tecla", "Shift+V"), out vkTalk, out talkShift, out talkCtrl, out talkAlt, out n);
            if (vkTalk == 0) vkTalk = (int)Keys.Menu;
            ParseCombo(cfg.Get("TeclaLimpiar", "Shift+G"), out vkClear, out clearSh, out clearCt, out clearAl, out n);
            ParseCombo(cfg.Get("TeclaAyuda", "Shift+H"), out vkHelp, out helpSh, out helpCt, out helpAl, out n);
            ParseCombo(cfg.Get("TeclaNombres", "Shift+M"), out vkNames, out namesSh, out namesCt, out namesAl, out n);
            ParseCombo(cfg.Get("TeclaRueda", "Shift+R"), out vkWheel, out wheelSh, out wheelCt, out wheelAl, out n);
            ParseCombo(cfg.Get("TeclaDeshacer", "Ctrl+Z"), out vkUndo, out undoSh, out undoCt, out undoAl, out n);
            ParseCombo(cfg.Get("TeclaBuild", "Shift+B"), out vkBuild, out buildSh, out buildCt, out buildAl, out n);
            { int v; ParseCombo(cfg.Get("TeclaMapaFoto", "MButton"), out v, out mapOcrSh, out mapOcrCt, out mapOcrAl, out n); vkMapOcr = v; }
            StartMouseWatch();
            SetTypeKey(cfg.Get("TeclaEscribir", "Shift+T"));
            st.ClearKeyName = cfg.Get("TeclaLimpiar", "Shift+G");
        }

        // accion -> (clave de config, valor por defecto, se puede dejar sin tecla)
        static readonly Tuple<string, string, string, bool>[] KeyActions = {
            Tuple.Create(L("Hablar (mantener)", "Talk (hold)"), "Tecla", "Shift+V", false),
            Tuple.Create(L("Escribir", "Type"), "TeclaEscribir", "Shift+T", true),
            Tuple.Create(L("Borrar lista (mantener 1 s)", "Clear list (hold 1 s)"), "TeclaLimpiar", "Shift+G", true),
            Tuple.Create(L("Ayuda (mantener)", "Help (hold)"), "TeclaAyuda", "Shift+H", true),
            Tuple.Create(L("Mostrar nombres", "Show names"), "TeclaNombres", "Shift+M", true),
            Tuple.Create(L("Rueda (con el menú ESC)", "Wheel (with the ESC menu)"), "TeclaRueda", "Shift+R", true),
            Tuple.Create(L("Deshacer", "Undo"), "TeclaDeshacer", "Ctrl+Z", true),
            Tuple.Create(L("Otra build sugerida", "Next suggested build"), "TeclaBuild", "Shift+B", true),
            Tuple.Create(L("Leer la pantalla (mapa y perks)", "Read the screen (map and perks)"), "TeclaMapaFoto", "MButton", true),
        };

        void AskKey(Tuple<string, string, string, bool> act)
        {
            if (keyCapture != null && !keyCapture.IsDisposed) keyCapture.Close();
            keyCapture = new KeyCapture(act.Item1, cfg.Get(act.Item2, act.Item3), act.Item4);
            keyCapture.Done += combo =>
            {
                // que no choque con otra accion
                foreach (var o in KeyActions)
                    if (o.Item2 != act.Item2 && combo.Length > 0 && string.Equals(cfg.Get(o.Item2, o.Item3), combo, StringComparison.OrdinalIgnoreCase))
                    { Feedback(combo + L(" ya es la tecla de \"", " is already the key for \"") + o.Item1 + "\"", Color.FromArgb(240, 170, 80), 5000); return; }
                SetCfg(act.Item2, combo);
                LoadKeys();
                Feedback(act.Item1 + ": " + (combo.Length > 0 ? combo : L("sin tecla", "no key")), Color.FromArgb(150, 220, 150), 4000);
                Relayout();
            };
            keyCapture.Show();
            keyCapture.Activate();
        }

        // ventanita: "apretá la combinación". Toma modificadores + tecla, un modificador solo, o un boton del mouse
        class KeyCapture : Form
        {
            public event Action<string> Done;
            readonly Label lbl, sub;
            readonly bool allowNone;
            bool other; // se apreto algo ademas de los modificadores

            public KeyCapture(string action, string current, bool canBeNone)
            {
                allowNone = canBeNone;
                FormBorderStyle = FormBorderStyle.FixedToolWindow; Text = L("Tecla: ", "Key: ") + action; StartPosition = FormStartPosition.CenterScreen;
                TopMost = true; ShowInTaskbar = false; KeyPreview = true; Width = 440; Height = 170;
                BackColor = Color.FromArgb(18, 18, 24);
                lbl = new Label { Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                                  Text = current.Length > 0 ? current : L("(sin tecla)", "(no key)") };
                sub = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(150, 150, 165), Font = new Font("Segoe UI", 9f),
                                  Text = L("Apretá la combinación nueva (por ejemplo Shift+V o un botón lateral del mouse).\nEsc: cancelar", "Press the new combination (for example Shift+V or a side mouse button).\nEsc: cancel") + (canBeNone ? L(" · Supr: sin tecla", " · Del: no key") : "") };
                Controls.Add(sub); Controls.Add(lbl);
                KeyUp += OnUp;
                MouseDown += OnMouse; lbl.MouseDown += OnMouse; sub.MouseDown += OnMouse;
            }

            static string Mods(Keys m)
            {
                string s = "";
                if ((m & Keys.Control) != 0) s += "Ctrl+";
                if ((m & Keys.Shift) != 0) s += "Shift+";
                if ((m & Keys.Alt) != 0) s += "Alt+";
                return s;
            }
            static bool IsMod(Keys k) { return k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LMenu || k == Keys.RMenu; }

            void Finish(string combo) { var d = Done; Close(); if (d != null) d(combo); }

            void OnDown(object s, KeyEventArgs e)
            {
                e.Handled = e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None) { Close(); return; }
                if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None && allowNone) { Finish(""); return; }
                if (IsMod(e.KeyCode)) { other = false; lbl.Text = Mods(e.Modifiers) + "…"; return; }
                other = true;
                Finish(Mods(e.Modifiers) + e.KeyCode);
            }

            // un modificador solo (por ejemplo "Alt" para hablar): se toma al soltarlo sin haber apretado otra tecla
            void OnUp(object s, KeyEventArgs e)
            {
                e.Handled = true;
                if (!IsMod(e.KeyCode) || other) return;
                string m = e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey ? "Shift"
                         : e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.LControlKey || e.KeyCode == Keys.RControlKey ? "Ctrl" : "Alt";
                if (e.Modifiers == Keys.None) Finish(m);
            }

            // botones del mouse que no son el izquierdo ni el derecho (el del medio y los laterales)
            void OnMouse(object s, MouseEventArgs e)
            {
                string b = e.Button == MouseButtons.XButton1 ? "XButton1" : e.Button == MouseButtons.XButton2 ? "XButton2" : e.Button == MouseButtons.Middle ? "MButton" : null;
                if (b == null) return;
                Finish(Mods(ModifierKeys) + b);
            }


            // todas las teclas (Esc, Enter, flechas, Alt, F10…) pasan por aca antes de que Windows las use para la ventana
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (msg.Msg == 0x100 || msg.Msg == 0x104) { OnDown(this, new KeyEventArgs(keyData)); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        // ---- escribir en vez de hablar: cuadro con autocompletado (solo con el cursor libre: menu de ESC, lobby, fuera del juego)
        int vkType; bool typeKeyDown, typeShift, typeCtrl, typeAlt, typePending;
        string typeKeyName = "Shift+T";

        // "Shift+T", "Ctrl+T", "Alt+T", "Ctrl+Shift+T" o una tecla sola ("T")
        void SetTypeKey(string spec) { ParseCombo(spec, out vkType, out typeShift, out typeCtrl, out typeAlt, out typeKeyName); }

        // "Shift+V", "Ctrl+T", "Alt+T", "Ctrl+Shift+T" o una tecla sola ("V")
        static void ParseCombo(string spec, out int vk, out bool sh, out bool ct, out bool al, out string name)
        {
            var parts = (spec ?? "").Split('+').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Count == 0) { vk = 0; sh = ct = al = false; name = ""; return; } // sin tecla (antes quedaba la T por error)
            sh = ct = al = false;
            foreach (var m in parts.Take(parts.Count - 1))
            {
                string l = m.ToLowerInvariant();
                if (l == "shift" || l == "mayus") sh = true;
                else if (l == "ctrl" || l == "control") ct = true;
                else if (l == "alt") al = true;
            }
            vk = Vk(parts[parts.Count - 1]);
            name = string.Join("+", parts);
        }

        // los modificadores exactos (si la tecla misma es Shift/Ctrl/Alt, no se mira)
        static bool ModsMatch(int vk, bool sh, bool ct, bool al)
        {
            if (vk >= 0x10 && vk <= 0x12 || vk >= 0xA0 && vk <= 0xA5) return true;
            bool s = (GetAsyncKeyState(0x10) & 0x8000) != 0, c = (GetAsyncKeyState(0x11) & 0x8000) != 0, a = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            return s == sh && c == ct && a == al;
        }

        // la combinacion exacta: con Shift+T, la T sola o Ctrl+Shift+T no la abren
        bool TypeComboDown() { return HotKey(vkType) && ModsMatch(vkType, typeShift, typeCtrl, typeAlt); }
        TypeBox typeBox;
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(int idAttach, int idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();

        bool CursorFree()
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            bool cursor = GetCursorInfo(ref ci) && (ci.flags & 1) != 0 && ci.hCursor != IntPtr.Zero;
            return cursor || !DbdInFront();
        }

        // ---- escribir sin sacarle el foco al juego: mientras el cuadro esta abierto, la app toma las teclas
        // (gancho de teclado de Windows, el mismo que usan Discord o AutoHotkey; no toca el juego) y no se las pasa al juego.
        // Solo existe mientras el cuadro esta abierto: cerrado, no hay gancho.
        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(int thread);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buf, int size, uint flags, IntPtr hkl);

        IntPtr kbHook;
        LowLevelKeyboardProc kbProc; // referencia viva (si no, el recolector de basura la borra y Windows llama a la nada)

        void StartKeyHook()
        {
            if (kbHook != IntPtr.Zero) return;
            kbProc = KeyHookProc;
            kbHook = SetWindowsHookEx(13, kbProc, GetModuleHandle(null), 0);
            if (kbHook == IntPtr.Zero) DebugEvent("escribir: no pude tomar el teclado (error " + Marshal.GetLastWin32Error() + ")");
        }

        void StopKeyHook()
        {
            if (kbHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(kbHook);
            kbHook = IntPtr.Zero;
        }

        static bool IsModifierVk(uint vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x14 || vk == 0x5B || vk == 0x5C || vk == 0x90 || vk == 0x91;
        }

        IntPtr KeyHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && typeBox != null && typeBox.Visible)
                {
                    var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool injected = (k.flags & 0x10) != 0;
                    // Shift, Ctrl, Alt, Bloq Mayus y Windows pasan (sirven para las mayusculas y combinaciones)
                    bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                    if (!injected && !IsModifierVk(k.vkCode) && !altDown)
                    {
                        int msg = wParam.ToInt32();
                        if (msg == 0x100 || msg == 0x104) // tecla apretada (tambien con Alt)
                        {
                            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0, ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0, alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                            var state = new byte[256];
                            if (shift) state[0x10] = 0x80;
                            if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
                            if (ctrl && alt) { state[0x11] = 0x80; state[0x12] = 0x80; } // AltGr (por ejemplo @)
                            int pid;
                            var hkl = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out pid));
                            var sb = new StringBuilder(8);
                            int r = (ctrl && !alt) ? 0 : ToUnicodeEx(k.vkCode, k.scanCode, state, sb, sb.Capacity, 0, hkl);
                            string text = r > 0 ? sb.ToString(0, r) : "";
                            var key = (Keys)k.vkCode;
                            var box = typeBox;
                            BeginInvoke((Action)(() => { if (box.Visible) box.HookKey(key, text, shift, ctrl); }));
                        }
                        return (IntPtr)1; // el juego no la recibe (ni apretada ni soltada)
                    }
                }
            }
            catch { }
            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        IntPtr typePrevFg, typeRefFg; bool typeFromGame; int typeOpenedAt;
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);

        // cerrar el cuadro (Enter / Esc): el foco vuelve directo a la ventana de antes (el juego).
        // Sin esto Windows elige la siguiente ventana y DBD a veces queda "sin foco" hasta hacerle clic.
        public void HideTypeBox() { HideTypeBox(null); }
        public void HideTypeBox(string why)
        {
            StopKeyHook();
            if (typeBox == null) return;
            if (typeBox.Visible) DebugEvent("escribir: cerrado" + (why != null ? " (" + why + ")" : ""));
            if (feedback.StartsWith("⌨ escribiendo") || feedback.StartsWith("⌨ typing")) { feedback = ""; Relayout(); }
            bool hadFocus = GetForegroundWindow() == typeBox.Handle;
            typeBox.Hide();
            Kick(); HoverTick();
            // solo si por algun motivo el cuadro quedo con el foco (por ejemplo, abierto desde el menu): devolverlo
            var prev = typePrevFg;
            if (hadFocus && prev != IntPtr.Zero && IsWindow(prev)) { SetForegroundWindow(prev); BringWindowToTop(prev); }
        }

        void OpenTypeBox()
        {
            if (typeBox == null || typeBox.IsDisposed) typeBox = new TypeBox(this);
            // primero el aviso (ensancha la lista) y despues el cuadro al costado
            Feedback(L("⌨ escribiendo… las teclas van al cuadro · Esc para cerrar", "⌨ typing… keys go to the box · Esc to close"), Color.FromArgb(240, 200, 110), int.MaxValue / 2);
            PlaceTypeBox();
            // el juego (o la ventana que este al frente) se queda con el foco: las teclas las toma el gancho
            var prevFg = GetForegroundWindow();
            typePrevFg = prevFg != typeBox.Handle && prevFg != Handle ? prevFg : IntPtr.Zero;
            typeFromGame = DbdInFront();
            typeOpenedAt = Environment.TickCount; typeRefFg = GetForegroundWindow();
            typeBox.Open();
            StartKeyHook();
            SetWindowPos(typeBox.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // arriba de todo (tambien del juego), sin activarse
            DebugEvent("escribir: abierto en " + typeBox.Bounds + (typeFromGame ? " (DBD al frente)" : "") + (kbHook == IntPtr.Zero ? " · SIN GANCHO" : ""));
        }

        // el cuadro va pegado al costado de la lista; si la lista cambia de ancho, se corre con ella
        void PlaceTypeBox()
        {
            if (typeBox == null || typeBox.IsDisposed) return;
            var b = Bounds;
            bool right = !cfg.Get("Posicion", "arriba-derecha").ToLowerInvariant().Contains("izq");
            var scr = Screen.FromRectangle(b).WorkingArea;
            int x = right ? b.Left - typeBox.Width - 8 : b.Right + 8;
            x = Math.Max(scr.Left, Math.Min(x, scr.Right - typeBox.Width));
            var loc = new Point(x, Math.Max(scr.Top, Math.Min(b.Top, scr.Bottom - typeBox.Height)));
            if (typeBox.Location != loc) typeBox.Location = loc;
        }

        // sugerencias: killers, perks, addons (los del killer anotado) y ofrendas, en ingles o en espanol
        public List<Entry> Suggest(string q)
        {
            string n = Txt.Norm(q);
            if (n.Length == 0) return new List<Entry>();
            var mm = Regex.Match(n, @"^(?:mapa|mapas|map|maps)\b\s*(.*)$");
            if (mm.Success)
            {
                string mr = mm.Groups[1].Value.Trim();
                var ml = new List<Entry>();
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "ocultar", "sacar", "cerrar", "quitar", "hide", "close", "off"))) ml.Add(new Entry { K = Kind.Offering, Name = L("Ocultar mapa", "Hide map"), Action = "mapa-ocultar", ActionTag = mapShown });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "grande", "agrandar", "mas", "zoom", "tamano", "bigger", "big", "size"))) ml.Add(new Entry { K = Kind.Offering, Name = L("Mapa más grande", "Bigger map"), Action = "mapa-mas", ActionTag = MapSize + " px" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "chico", "achicar", "menos", "pequeno", "tamano", "smaller", "small", "size"))) ml.Add(new Entry { K = Kind.Offering, Name = L("Mapa más chico", "Smaller map"), Action = "mapa-menos", ActionTag = MapSize + " px" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "transparente", "opacidad", "clarito", "invisible", "transparent", "opacity"))) ml.Add(new Entry { K = Kind.Offering, Name = L("Mapa más transparente", "More transparent map"), Action = "mapa-transp", ActionTag = MapOpacity + " %" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "opaco", "solido", "opacidad", "visible", "opaque", "solid", "opacity"))) ml.Add(new Entry { K = Kind.Offering, Name = L("Mapa más opaco", "More opaque map"), Action = "mapa-opaco", ActionTag = MapOpacity + " %" });
                ml.AddRange(cat.Maps.Where(m => mr.Length == 0 || m.Keys.Any(k => k.N.Contains(mr)) || Txt.Norm(m.ActionTag ?? "").Contains(mr)).OrderBy(m => m.Name));
                return ml;
            }
            var acts = RoomActions(n);
            if (acts.Count > 0) { var rest0 = SuggestInner(q, n); acts.AddRange(rest0); return acts; }
            return SuggestInner(q, n);
        }

        // ---- acciones de sala como opciones de la lista: "salir"/"exit", "sala"/"nueva", "sala CODIGO"
        static bool Kw(string n, params string[] words)
        {
            var toks = n.Split(' ');
            if (toks.Length > 2) return false; // frases largas: es otra cosa
            return toks.Any(tk => words.Any(w => w == tk || (tk.Length >= 3 && w.StartsWith(tk))));
        }
        List<Entry> RoomActions(string n)
        {
            var l = new List<Entry>();
            string code = "";
            var sm = Regex.Match(n, @"^(?:sala|room|entrar(?: a)?(?: la)?(?: sala)?|unirse|join)\s+([a-z0-9 ]{6,})$");
            if (sm.Success && !Regex.IsMatch(sm.Groups[1].Value, @"^(nueva|crear|crea|new|salir|dejar|leave)$")) code = Room.CleanCode(sm.Groups[1].Value);
            if (code.Length >= 6)
            {
                l.Add(new Entry { K = Kind.Offering, Name = L("Entrar a la sala ", "Join room ") + code, Action = "entrar:" + code, ActionTag = ManualRoom ? L("sale de la actual", "leaves the current one") : "" });
                return l;
            }
            bool wantExit = Kw(n, "salir", "exit", "leave", "dejar", "irme"), wantNew = Kw(n, "nueva", "crear", "new");
            bool anyRoom = Kw(n, "sala", "room");
            bool salir = ManualRoom && (wantExit || (anyRoom && !wantNew));
            bool nueva = wantNew || (anyRoom && !wantExit);
            if (salir) l.Add(new Entry { K = Kind.Offering, Name = L("Salir de la sala ", "Leave room ") + cfg.Get("Sala", ""), Action = "salir", ActionTag = RoomCount > 0 ? RoomCount + "/" + MaxRoom + L(" adentro", " inside") : "" });
            if (nueva) l.Add(new Entry { K = Kind.Offering, Name = L("Crear sala nueva", "Create new room"), Action = "nueva", ActionTag = L("copia el código para el chat", "copies the code for the chat") });
            return l;
        }
        public void DoAction(string a)
        {
            if (a.StartsWith("mapa:")) { ShowMap(a.Substring(5), true); return; }
            if (a == "mapa-ocultar") { HideMap(true); return; }
            if (a == "mapa-mas") { StepMapSize(1); return; }
            if (a == "mapa-menos") { StepMapSize(-1); return; }
            if (a == "mapa-transp") { StepMapOpacity(-1); return; }
            if (a == "mapa-opaco") { StepMapOpacity(1); return; }
            if (a == "salir") { if (ManualRoom) LeaveRoom(); }
            else if (a == "nueva") BoxRoom("sala nueva");
            else if (a.StartsWith("entrar:")) BoxRoom("sala " + a.Substring(7));
        }

        List<Entry> SuggestInner(string q, string n)
        {
            // "quitar …", "cambiar …", "duda …": lo que ya esta anotado
            var bm = BoxModeRx.Match(n);
            if (bm.Success) return CurrentMatching(bm.Groups[2].Value, null);
            // "addon [color] [palabra]": todos los addons del killer anotado, de mejor (rojo) a peor (marron)
            var am = Regex.Match(n, @"^(?:addons?|add ons?|adon|accesorios?)\b\s*(.*)$");
            if (am.Success)
            {
                string rest = am.Groups[1].Value.Trim();
                // sin killer anotado: se busca en los addons de todos (solo si se escribio algo para filtrar)
                if (st.Killer == null && rest.Length == 0) return new List<Entry>();
                IEnumerable<Entry> list = st.Killer != null ? cat.Addons.Where(a => a.Killer == st.Killer.Name) : cat.Addons;
                var cm = State.ColorRx.Match(rest);
                if (cm.Success)
                {
                    string rar = State.ColorRarity(cm.Value);
                    list = list.Where(a => a.Rarity == rar || (rar == "visceral" && a.Rarity == "iridescent"));
                    rest = rest.Remove(cm.Index, cm.Length).Trim();
                }
                // palabras y numeros que esten en el nombre o la descripcion ("addon 15 lento"): primero los que tienen todas
                return EffectSearch(list, rest, l => l.OrderBy(a => RarityRank(a.Rarity)).ThenBy(a => DName(a)));
            }
            // "perk [efecto]": perks cuya descripcion coincide ("perk 4 regresion", "perk aura gancho")
            var pm = Regex.Match(n, @"^(?:perks?|habilidad(?:es)?)\s+(.+)$");
            if (pm.Success)
            {
                var kn = st.Killer != null ? st.Killer.Name : null;
                // los del killer anotado primero, despues los generales y los de otros killers
                return EffectSearch(cat.Perks, pm.Groups[1].Value.Trim(), l => l.OrderBy(p => p.Owner == kn && kn != null ? 0 : string.IsNullOrEmpty(p.Owner) ? 1 : 2).ThenBy(p => DName(p)));
            }
            var addons = st.Killer != null ? cat.Addons.Where(a => a.Killer == st.Killer.Name) : cat.Addons;
            var pool = cat.Killers.Concat(cat.Perks).Concat(addons).Concat(cat.Offerings).Concat(cat.Maps);
            var res = pool.Select(e =>
            {
                double s = 0;
                foreach (var nm in new[] { e.Name, e.NameEs }.Where(x => !string.IsNullOrEmpty(x)).Select(x => Txt.Norm(x)).Concat(e.Keys.Select(k => k.N)))
                {
                    if (nm == n || (" " + nm + " ").Contains(" " + n + " ")) s = Math.Max(s, 3.3); // palabra entera ("pain" -> Pain Resonance)
                    else if (nm.StartsWith(n)) s = Math.Max(s, 3);
                    else if ((" " + nm).Contains(" " + n)) s = Math.Max(s, 2.5);
                    else if (nm.Contains(n)) s = Math.Max(s, 2);
                    else if (n.Length >= 3) { double r = Txt.Ratio(n, nm.Substring(0, Math.Min(nm.Length, n.Length))); if (r >= 0.75) s = Math.Max(s, r); }
                }
                // a igual coincidencia: perks y addons del killer primero, ofrendas al final
                if (s > 0) s += e.K == Kind.Perk ? 0.1 : e.K == Kind.Addon && st.Killer != null ? 0.1 : e.K == Kind.Offering ? -0.1 : 0;
                return new { E = e, S = s };
            }).Where(x => x.S > 0).OrderByDescending(x => x.S).ThenBy(x => x.E.Name.Length).Take(8).Select(x => x.E).ToList();
            // nombre de un killer: sus perks justo debajo (para cuando sabes de quien es el perk pero no como se llama)
            var kl = res.FirstOrDefault(e => e.K == Kind.Killer);
            if (kl != null)
            {
                var own = cat.Perks.Where(p => p.Owner == kl.Name).OrderBy(p => DName(p)).ToList();
                res.RemoveAll(e => own.Contains(e));
                res.InsertRange(res.IndexOf(kl) + 1, own);
            }
            return res;
        }

        // palabras de relleno ("addon que me hace 15 mas lento")
        static readonly HashSet<string> AddonFiller = new HashSet<string>(("que me te le lo la el los las un una de del y o a en por con mas menos hace hacia " +
            "tiene tenia porciento ciento porcentaje cuando al su sus se es").Split(' '));
        // como lo decimos -> como lo dice la descripcion (en castellano y en ingles)
        static readonly Dictionary<string, string[]> AddonSyn = new Dictionary<string, string[]>
        {
            { "lento", new[] { "obstaculiz", "hinder", "ralentiz", "slow", "lentitud", "reduce la velocidad", "decreases" } },
            { "ralentiza", new[] { "obstaculiz", "hinder", "ralentiz", "slow", "lentitud", "reduce la velocidad", "decreases" } },
            { "slow", new[] { "obstaculiz", "hinder", "ralentiz", "slow", "lentitud", "decreases" } },
            { "obstaculizado", new[] { "obstaculiz", "hinder" } },
            { "rapido", new[] { "velocidad", "speed", "rapid", "acelera", "increases movement" } },
            { "velocidad", new[] { "velocidad", "speed" } },
            { "fast", new[] { "speed", "haste", "faster", "increases movement", "velocidad" } },
            { "quick", new[] { "speed", "haste", "faster", "velocidad" } },
            { "bleed", new[] { "haemorrhag", "hemorrhag", "bleed", "hemorrag" } },
            { "hurt", new[] { "injur", "mangled", "deep wound", "herid" } },
            { "faster", new[] { "speed", "haste", "faster" } },
            { "ciego", new[] { "ceguera", "ciego", "blind" } },
            { "agotado", new[] { "agotamiento", "exhaust" } },
            { "herido", new[] { "destroz", "mangled", "herid", "injur" } },
            { "sangra", new[] { "hemorrag", "haemorrhag", "hemorrhag" } },
            { "grito", new[] { "grit", "scream" } },
            { "indetectable", new[] { "indetectable", "undetectable" } },
            { "terror", new[] { "terror" } },
            { "aura", new[] { "aura" } },
            { "expuesto", new[] { "expuest", "exposed" } },
            { "rompe", new[] { "romp", "rot", "broken", "break" } },
            { "reparar", new[] { "repar", "repair", "generador", "generator", "regres" } },
            { "duracion", new[] { "duracion", "duration" } },
            { "regresion", new[] { "regres", "regress" } },
            { "gancho", new[] { "gancho", "colg", "hook" } },
            { "generador", new[] { "generador", "generator" } },
            { "totem", new[] { "totem", "hex", "maleficio" } },
            { "obsesion", new[] { "obsesion", "obsession" } },
            { "curar", new[] { "cura", "heal" } },
            { "bloquea", new[] { "bloque", "block" } },
            { "ventana", new[] { "ventana", "window", "vault", "salt" } },
            { "palet", new[] { "palet", "pallet" } },
            { "taquilla", new[] { "taquilla", "locker" } },
            { "puerta", new[] { "puerta", "salida", "exit gate", "gate" } },
            { "endgame", new[] { "fin de partida", "colapso", "end game", "endgame", "collapse" } },
            { "ruido", new[] { "ruido", "noise", "notificacion", "notification" } },
            { "ataque", new[] { "ataque", "attack", "golpe", "hit" } },
            { "instakill", new[] { "matar", "mori", "kill" } },
            { "oneshot", new[] { "estado de agonia", "dying state", "golpe basico" } },
            { "recarga", new[] { "recarg", "reload", "enfriamiento", "cooldown" } },
        };
        static IEnumerable<string> AddonWordAlts(string w)
        {
            yield return w;
            foreach (var kv in AddonSyn)
                if (w.StartsWith(kv.Key.Substring(0, Math.Min(kv.Key.Length, 4))) && w.Length >= 4 || w == kv.Key)
                    foreach (var alt in kv.Value) yield return alt;
        }

        // busca palabras y numeros en el nombre y la descripcion: primero los que tienen todas (o la mayor cantidad)
        List<Entry> EffectSearch(IEnumerable<Entry> list, string rest, Func<IEnumerable<Entry>, IEnumerable<Entry>> order)
        {
            var words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !AddonFiller.Contains(w)).ToList();
            if (words.Count == 0) return order(list).ToList();
            var scored = list.Select(a =>
            {
                string txt = " " + Txt.Norm(a.Name + " " + a.NameEs + " " + a.Info + " " + a.InfoEn) + " ";
                int hits = words.Count(w => AddonWordAlts(w).Any(alt => Regex.IsMatch(alt, "^[0-9]+$") ? txt.Contains(" " + alt + " ") : txt.Contains(alt)));
                return new { A = a, H = hits };
            }).Where(x => x.H > 0).ToList();
            if (scored.Count == 0) return new List<Entry>();
            int best = scored.Max(x => x.H);
            return order(scored.Where(x => x.H == best).Select(x => x.A)).Take(30).ToList();
        }

        public bool HasKiller { get { return st.Killer != null; } }
        public Entry CurrentKiller { get { return st.Killer; } }
        public void ConfirmKiller(Entry k)
        {
            st.ConfirmedKiller = true;
            try { SubmitTyped("", k, false, true); } finally { st.ConfirmedKiller = false; }
        }
        static int RarityRank(string r)
        {
            switch (r) { case "iridescent": return 0; case "visceral": return 1; case "veryrare": return 2; case "rare": return 3; case "uncommon": return 4; case "common": return 5; default: return 6; }
        }

        public string SuggestTag(Entry e)
        {
            switch (e.K)
            {
                case Kind.Killer: return "";
                case Kind.Perk: return !string.IsNullOrEmpty(e.Owner) ? L("de ", "") + e.Owner : L("general", "general");
                case Kind.Offering: return "";
                default: return (e.Killer ?? "") + (e.Rarity != null && ColorNameOf(e.Rarity).Length > 0 ? " · " + ColorNameOf(e.Rarity) : "");
            }
        }
        static string ColorNameOf(string rar)
        {
            if (Lang.En) switch (rar) { case "visceral": return "red"; case "veryrare": return "purple"; case "rare": return "green"; case "uncommon": return "yellow"; case "common": return "brown"; default: return ""; }
            switch (rar) { case "visceral": return "rojo"; case "veryrare": return "violeta"; case "rare": return "verde"; case "uncommon": return "amarillo"; case "common": return "marrón"; default: return ""; }
        }

        static readonly Regex TypedCmdRx = new Regex(@"^(?:tiene|tenia|quitar?|sacar?|borrar?|creo|probablemente|capaz|el killer|killer|ofrenda|no tiene|confirmad|descartad|el (?:uno|dos|tres|cuatro|cinco|[1-5])|has|have|got|remove|delete|i think|maybe|probably|the killer|offering|confirm|confirmed|discard|discarded|undo|none|the (?:one|two|three|four|five|first|second|third)|number)\b");

        // Enter: anota la sugerencia elegida (o, si es un comando, lo aplica como si lo hubiera dicho)
        public void SubmitTyped(string text, Entry picked, bool doubt, bool byArrows)
        {
            text = (text ?? "").Trim();
            var before = st.Snapshot();
            string what;
            bool isCmd = TypedCmdRx.IsMatch(Txt.Norm(text));
            if (picked != null && (!isCmd || byArrows))
                what = st.AddPicked(picked, doubt);
            else if (text.Length > 0)
            {
                st.Trace.Clear();
                what = st.Apply(doubt ? "creo que " + text : text);
                st.ForgetGuess();
            }
            else return;
            string said = picked != null && (!isCmd || byArrows) ? picked.Name : text;
            Heard("texto", said, what, false);
            // en el historial, lo que se escribio y lo que se eligio ("bbq?" -> Barbecue & Chili), para ver de donde sale cada cosa
            Log("[texto] " + (picked != null && (!isCmd || byArrows) && text.Length > 0 ? "\"" + text + "\" → " + picked.Name : said), what);
            bool ok = !Lang.Fail(what);
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Feedback("⌨ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }

        // ---- quitar / cambiar / duda desde el cuadro
        static readonly Regex BoxModeRx = new Regex(@"^(quitar?|sacar?|borrar?|cambiar?|reemplazar?|duda|dudo|sospecha|seguro|confirmar?|remove|delete|change|replace|swap|doubt|guess|sure|confirm)\b\s*(.*)$");
        public string BoxMode(string text)
        {
            var m = BoxModeRx.Match(Txt.Norm(text ?? ""));
            if (!m.Success) return "";
            string w = m.Groups[1].Value;
            return w.StartsWith("camb") || w.StartsWith("reemp") || w == "change" || w == "replace" || w == "swap" ? "cambiar" : w.StartsWith("dud") || w == "sospecha" || w == "seguro" || w.StartsWith("confirm") || w == "doubt" || w == "guess" || w == "sure" ? "duda" : "quitar";
        }

        // lo anotado (o solo de un tipo), filtrado por lo que se escribio
        public List<Entry> CurrentMatching(string rest, Kind? kind)
        {
            string n = Txt.Norm(rest ?? "");
            return st.Current().Where(e => (kind == null || e.K == kind) &&
                (n.Length == 0 || Txt.Norm(e.Name).Contains(n) || (e.NameEs != null && Txt.Norm(e.NameEs).Contains(n)) || e.Keys.Any(k => k.N.Contains(n)))).ToList();
        }
        public bool IsFull(Entry e) { return st.IsFull(e); }
        public bool IsDoubt(Entry e) { return st.IsDoubt(e); }

        public void BoxDo(string said, Func<string> act, string src = "texto")
        {
            var before = st.Snapshot();
            string what = act();
            Heard(src, said, what, false);
            Log("[" + src + "] " + said, what);
            bool ok = !Lang.Fail(what);
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Feedback("⌨ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }
        public List<Entry> CurrentPicked() { return st.Current(); }
        // lo que se quita (o se cambia por otro) a proposito no vuelve a aparecer como sugerido de la build
        public void BoxRemove(Entry e) { ghostOff.Add(e); BoxDo("quitar " + e.Name, () => st.RemovePicked(e)); }
        public void BoxReplace(Entry old, Entry neu, bool doubt) { ghostOff.Add(old); BoxDo("cambiar " + old.Name + " por " + neu.Name, () => st.ReplacePicked(old, neu, doubt)); }
        public void BoxToggleDoubt(Entry e) { BoxDo("duda " + e.Name, () => st.ToggleDoubt(e)); }

        class TypeBox : Form
        {
            readonly Overlay ov;
            readonly TextBox tb;
            readonly ListBox lb;
            readonly Label hint;
            List<Entry> sugg = new List<Entry>();
            bool byArrows;

            public TypeBox(Overlay o)
            {
                ov = o;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
                BackColor = Color.FromArgb(18, 18, 24); Padding = new Padding(8);
                Opacity = 0.97; // ventana en capas, como el widget y el hover (se ve encima del juego igual que ellos)
                Width = 440;
                tb = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(32, 32, 42), ForeColor = Color.WhiteSmoke, Font = new Font("Segoe UI", 12f) };
                lb = new ListBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(18, 18, 24), ForeColor = Color.Gainsboro,
                                   DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24, Height = 0, IntegralHeight = false, Font = new Font("Segoe UI", 10.5f) };
                hint = new Label { Dock = DockStyle.Top, Height = 36, ForeColor = Color.FromArgb(140, 140, 155), Font = new Font("Segoe UI", 8.5f),
                                   Text = HintText };
                Controls.Add(hint); Controls.Add(lb); Controls.Add(tb);
                tb.TextChanged += (s, e) => Refresh2();
                lb.DrawItem += DrawSugg;
                lb.MouseClick += (s, e) => { int i = lb.IndexFromPoint(e.Location); if (i >= 0) { lb.SelectedIndex = i; byArrows = true; Submit(false); } };
                KeyDown += OnKey;
                Refresh2();
            }

            public void Open() { replaceTarget = replaceNew = null; tb.Text = ""; Refresh2(); Show(); }
            Entry replaceTarget; // "cambiar X": X elegido, ahora se escribe el nuevo
            Entry replaceNew;    // lista llena: el nuevo elegido, ahora se elige cual sale
            readonly Dictionary<Entry, Entry> removeOf = new Dictionary<Entry, Entry>(); // filas "quitar" -> lo anotado
            // no se activa al mostrarse ni al hacerle clic: el juego conserva el foco
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x8; return cp; } }

            // teclas que llegan del gancho (el cuadro no tiene el foco)
            public void HookKey(Keys key, string text, bool shift, bool ctrl)
            {
                var e = new KeyEventArgs(key | (shift ? Keys.Shift : 0) | (ctrl ? Keys.Control : 0));
                if (key == Keys.Escape || key == Keys.Enter || key == Keys.Up || key == Keys.Down || (key == Keys.Back && (shift || ctrl))) { OnKey(this, e); return; }
                if (key == Keys.Back) { if (tb.Text.Length > 0) SetText(tb.Text.Substring(0, tb.Text.Length - 1)); return; }
                if (ctrl && key == Keys.V) { try { string cb = Clipboard.GetText(); if (!string.IsNullOrEmpty(cb)) SetText(tb.Text + Regex.Replace(cb, @"\s+", " ").Trim()); } catch { } return; }
                if (ctrl && key == Keys.Z) { ov.UndoLast(); return; }
                if (ctrl) return;
                foreach (char c in text) if (!char.IsControl(c)) SetText(tb.Text + c);
            }
            string HintText { get { return L("Enter: anotar · \"noed?\" o Shift+Enter: sospecha · ↑↓ elegir · Esc: cerrar", "Enter: add · \"noed?\" or Shift+Enter: guess · ↑↓ choose · Esc: close") + (ov.HelpKeyName.Length > 0 ? " · " + ov.HelpKeyName + L(": ayuda", ": help") : "") + "\n" + HintRest; } }
            static string HintRest { get { return L("\"addon\" · \"addon 15 lento\" · \"perk aura gancho\" · \"sala\" · \"quitar …\" · \"cambiar …\" · \"duda …\"", "\"addon\" · \"addon 15 slow\" · \"perk aura hook\" · \"room\" · \"remove …\" · \"change …\" · \"doubt …\""); } }
            public void FocusText() { tb.Focus(); }
            public void SetText(string s) { tb.Text = s; tb.SelectionStart = s.Length; }

            string lastQuery = "";
            void Refresh2()
            {
                string why = null;
                // lo elegido con flechas antes de este cambio (si solo se agrego "?" o un espacio, se mantiene)
                Entry prevSel = lb.SelectedIndex >= 0 && lb.SelectedIndex < sugg.Count ? sugg[lb.SelectedIndex] : null;
                bool prevRemove = prevSel != null && removeOf.ContainsKey(prevSel), prevArrows = byArrows;
                string query = Txt.Norm(Regex.Replace(tb.Text ?? "", "[?¿]", " "));
                bool sameQuery = query == lastQuery;
                lastQuery = query;
                if (replaceNew != null)
                {
                    sugg = ov.CurrentMatching(tb.Text, replaceNew.K);
                    why = L("Ya está lleno: ¿cuál sacás para poner ", "It's full: which one goes out for ") + replaceNew.Name + L("?  (Enter: ese · Esc: nada)", "?  (Enter: that one · Esc: none)");
                }
                else if (replaceTarget != null)
                {
                    var rt = replaceTarget;
                    sugg = Txt.Norm(tb.Text).Length == 0 ? new List<Entry>() : ov.Suggest(tb.Text).Where(x => x.K == rt.K && x != rt && x.Action == null).ToList();
                    why = L("Cambiar ", "Change ") + rt.Name + L(" por…  (escribí el nuevo · Shift+Enter: como sospecha)", " to…  (type the new one · Shift+Enter: as a guess)");
                }
                else if (ov.BoxRoomHint(tb.Text) != null)
                {
                    sugg = ov.Suggest(tb.Text).Where(x => x.Action != null).ToList();
                    why = ov.BoxRoomHint(tb.Text);
                }
                else if (Txt.Norm(tb.Text).Length == 0)
                {
                    string qt;
                    sugg = ov.QuickPicks(out qt);
                    if (sugg.Count > 0) why = qt + L(" · ↓ y Enter para anotar (o escribí)", " · ↓ and Enter to add (or type)");
                }
                else
                {
                    sugg = ov.Suggest(tb.Text);
                    string mode = ov.BoxMode(tb.Text);
                    removeOf.Clear();
                    if (mode.Length == 0)
                    {
                        var cur = ov.CurrentPicked();
                        for (int i = 0; i < sugg.Count; i++)
                            if (cur.Contains(sugg[i]))
                            {
                                var o = sugg[i];
                                var q = new Entry { K = o.K, Name = o.Name, NameEs = o.NameEs, Icon = o.Icon, Rarity = o.Rarity, Killer = o.Killer };
                                removeOf[q] = o;
                                // sospecha: primero confirmarla y abajo quitar; ya confirmado: solo quitar (confirmar de nuevo no hace nada)
                                if (ov.IsDoubt(o)) sugg.Insert(++i, q); else sugg[i] = q;
                            }
                    }
                    if (mode == "quitar") why = L("Quitar: elegí cuál (Enter)", "Remove: choose which one (Enter)");
                    else if (mode == "cambiar") why = L("Cambiar: elegí cuál sale (Enter) y después escribí el nuevo", "Change: choose which one goes out (Enter), then type the new one");
                    else if (mode == "duda") why = L("Duda: Enter alterna entre seguro y sospecha (?)", "Doubt: Enter toggles between sure and guess (?)");
                    if (mode.Length > 0 && sugg.Count == 0) why = L("No hay nada anotado que coincida", "Nothing on the list matches");
                }
                confirmKiller = null;
                lb.Items.Clear();
                foreach (var e in sugg) lb.Items.Add(e);
                if (sugg.Count > 0 && (Txt.Norm(tb.Text).Length > 0 || replaceNew != null)) lb.SelectedIndex = 0; // cuadro vacio: nada elegido hasta bajar con flecha (salvo "cual sacas")
                byArrows = false;
                if (sameQuery && prevSel != null)
                {
                    int k = sugg.FindIndex(x => prevRemove ? removeOf.ContainsKey(x) && x.Name == prevSel.Name : x == prevSel);
                    if (k >= 0) { lb.SelectedIndex = k; byArrows = prevArrows; }
                }
                lb.Height = Math.Min(sugg.Count, 10) * lb.ItemHeight; // con mas de 10 aparece la barra para bajar
                if (sugg.Count > 0) lb.TopIndex = 0;
                bool noKiller = Regex.IsMatch(Txt.Norm(tb.Text), @"^(?:addons?|add ons?|adon|accesorios?)$") && !ov.HasKiller;
                hint.Text = noKiller ? L("Sin killer anotado: escribí algo de la descripción (\"addon 15 lento\") o anotá el killer primero.", "No killer on the list: type something from the description (\"addon 15 slow\") or add the killer first.") : why != null ? why + "\n" + HintText.Split('\n')[0] : HintText;
                hint.ForeColor = noKiller || (why != null && Txt.Norm(tb.Text).Length > 0) ? Color.FromArgb(240, 170, 80) : why != null ? Color.FromArgb(150, 200, 160) : Color.FromArgb(140, 140, 155);
                Height = Padding.Vertical + tb.Height + lb.Height + hint.Height + 4;
            }

            void DrawSugg(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= sugg.Count) return;
                var en = sugg[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Color.FromArgb(52, 52, 70) : Color.FromArgb(18, 18, 24))) e.Graphics.FillRectangle(b, e.Bounds);
                var icon = en.Icon != null ? ov.GetIcon(en.Icon) : null;
                int x = e.Bounds.X + 4;
                if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(x, e.Bounds.Y + 2, 20, 20));
                x += 26;
                // etiqueta de color con el tipo (KILLER / PERK / ADDON / OFRENDA), todas del mismo ancho para que los nombres queden alineados
                string kind; Color pill;
                if (removeOf.ContainsKey(en))
                {
                    // ya anotado: "QUITAR" en rojo y el nombre tachado
                    var qr = new Rectangle(x, e.Bounds.Y + 4, PillW, e.Bounds.Height - 8);
                    using (var b = new SolidBrush(Color.FromArgb(150, 35, 40))) e.Graphics.FillRectangle(b, qr);
                    TextRenderer.DrawText(e.Graphics, L("QUITAR", "REMOVE"), PillFont, qr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    int qx = x + PillW + 8;
                    string qn = ov.DName(en);
                    using (var sf = new Font(lb.Font, FontStyle.Strikeout))
                    {
                        TextRenderer.DrawText(e.Graphics, qn, sf, new Point(qx, e.Bounds.Y + 3), Color.FromArgb(240, 130, 130), TextFormatFlags.NoPrefix);
                        qx += TextRenderer.MeasureText(qn, sf).Width;
                    }
                    using (var f = new Font("Segoe UI", 8.5f))
                        TextRenderer.DrawText(e.Graphics, L("ya está anotado: Enter lo saca", "already on the list: Enter removes it"), f, new Point(qx + 10, e.Bounds.Y + 6), Color.FromArgb(130, 130, 145), TextFormatFlags.NoPrefix);
                    return;
                }
                if (en.Action != null)
                {
                    var ar = new Rectangle(x, e.Bounds.Y + 4, PillW, e.Bounds.Height - 8);
                    bool isMap = en.Action.StartsWith("mapa");
                    using (var b = new SolidBrush(isMap ? Color.FromArgb(70, 60, 120) : Color.FromArgb(30, 110, 70))) e.Graphics.FillRectangle(b, ar);
                    TextRenderer.DrawText(e.Graphics, isMap ? "MAPA" : "SALA", PillFont, ar, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    int ax = x + PillW + 8;
                    TextRenderer.DrawText(e.Graphics, en.Name, lb.Font, new Point(ax, e.Bounds.Y + 3), isMap ? Color.FromArgb(190, 180, 240) : Color.FromArgb(150, 225, 170), TextFormatFlags.NoPrefix);
                    ax += TextRenderer.MeasureText(en.Name, lb.Font).Width;
                    using (var f = new Font("Segoe UI", 8.5f))
                        TextRenderer.DrawText(e.Graphics, en.ActionTag ?? "", f, new Point(ax + 10, e.Bounds.Y + 6), Color.FromArgb(130, 130, 145), TextFormatFlags.NoPrefix);
                    return;
                }
                switch (en.K)
                {
                    case Kind.Killer: kind = "KILLER"; pill = Color.FromArgb(170, 40, 45); break;
                    case Kind.Perk:
                        if (TiersOn) { var tc = WheelWindow.TierColor(WheelWindow.PerkTier(en)); kind = "PERK " + WheelWindow.PerkTier(en); pill = Color.FromArgb(tc.R / 2, tc.G / 2, tc.B / 2); }
                        else { kind = "PERK"; pill = Color.FromArgb(70, 80, 120); }
                        break;
                    case Kind.Offering: kind = "OFRENDA"; pill = Color.FromArgb(140, 110, 40); break;
                    default: { var rc = RarityColor(en.Rarity); kind = "ADDON"; pill = Color.FromArgb(rc.R / 2, rc.G / 2, rc.B / 2); break; }
                }
                if (ov.IsDoubt(en)) { kind = L("CONFIRMAR", "CONFIRM"); pill = Color.FromArgb(40, 135, 70); } // sospecha anotada: Enter la confirma
                var pr = new Rectangle(x, e.Bounds.Y + 4, PillW, e.Bounds.Height - 8);
                using (var b = new SolidBrush(pill)) e.Graphics.FillRectangle(b, pr);
                TextRenderer.DrawText(e.Graphics, kind, PillFont, pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += PillW + 8;
                Color c = en.K == Kind.Killer ? Color.FromArgb(255, 120, 120) : en.K == Kind.Perk ? (TiersOn ? WheelWindow.TierColor(WheelWindow.PerkTier(en)) : Color.WhiteSmoke) : RarityColor(en.Rarity);
                string name = ov.DName(en);
                using (var nf = en.K == Kind.Killer ? new Font(lb.Font, FontStyle.Bold) : null)
                {
                    var f0 = nf ?? lb.Font;
                    TextRenderer.DrawText(e.Graphics, name, f0, new Point(x, e.Bounds.Y + 3), c, TextFormatFlags.NoPrefix);
                    x += TextRenderer.MeasureText(name, f0).Width;
                }
                using (var f = new Font("Segoe UI", 8.5f))
                    TextRenderer.DrawText(e.Graphics, (ov.IsDoubt(en) ? L("sospecha (?): Enter la confirma", "guess (?): Enter confirms it") : ov.SuggestTag(en)), f, new Point(x + 10, e.Bounds.Y + 6), Color.FromArgb(130, 130, 145), TextFormatFlags.NoPrefix);
            }
            static readonly Font PillFont = new Font("Segoe UI", 7f, FontStyle.Bold);
            bool TiersOn { get { return ov.cfg.Get("RuedaTiers", "si") != "no"; } } // el mismo color por tier que en la rueda
            const int PillW = 70;

            void OnKey(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { ov.HideTypeBox(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Back && (e.Shift || e.Control))
                {
                    // Shift/Ctrl+Retroceso: borra la palabra anterior de una (sin esto Windows escribe un caracter raro)
                    int end = tb.SelectionStart, start = end;
                    if (tb.SelectionLength > 0) { start = end; end += tb.SelectionLength; }
                    else
                    {
                        while (start > 0 && char.IsWhiteSpace(tb.Text[start - 1])) start--;
                        while (start > 0 && !char.IsWhiteSpace(tb.Text[start - 1])) start--;
                    }
                    tb.Text = tb.Text.Remove(start, end - start);
                    tb.SelectionStart = start;
                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down && sugg.Count > 0) { lb.SelectedIndex = Math.Min(sugg.Count - 1, lb.SelectedIndex + 1); byArrows = true; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up && sugg.Count > 0) { lb.SelectedIndex = Math.Max(0, lb.SelectedIndex - 1); byArrows = true; e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Submit(e.Shift); e.Handled = e.SuppressKeyPress = true; }
            }

            Entry confirmKiller; // eligio otro killer: el proximo Enter confirma el cambio

            void Submit(bool doubt)
            {
                // "noed?" (o "¿noed?"): un signo de pregunta en lo escrito = anotarlo como sospecha
                if (tb.Text.IndexOf('?') >= 0 || tb.Text.IndexOf('¿') >= 0) doubt = true;
                if (confirmKiller != null)
                {
                    ov.ConfirmKiller(confirmKiller);
                    confirmKiller = null; tb.Text = ""; ov.HideTypeBox();
                    return;
                }
                Entry pa = lb.SelectedIndex >= 0 && lb.SelectedIndex < sugg.Count ? sugg[lb.SelectedIndex] : null;
                Entry ro;
                if (pa != null && replaceTarget == null && replaceNew == null && removeOf.TryGetValue(pa, out ro)) { ov.BoxRemove(ro); Close2(); return; }
                if (replaceTarget == null && replaceNew == null && pa != null && pa.Action != null) { ov.DoAction(pa.Action); Close2(); return; }
                if (replaceTarget == null && replaceNew == null && ov.BoxRoomHint(tb.Text) != null)
                {
                    if (ov.BoxRoom(tb.Text)) Close2();
                    return;
                }
                Entry picked = lb.SelectedIndex >= 0 && lb.SelectedIndex < sugg.Count ? sugg[lb.SelectedIndex] : null;
                // modos de cambio
                if (replaceNew != null) { if (picked == null) return; ov.BoxReplace(picked, replaceNew, doubt || replaceDoubt); Close2(); return; }
                if (replaceTarget != null) { if (picked == null) return; ov.BoxReplace(replaceTarget, picked, doubt); Close2(); return; }
                string mode = ov.BoxMode(tb.Text);
                if (mode.Length > 0 && picked != null)
                {
                    if (mode == "quitar") { ov.BoxRemove(picked); Close2(); return; }
                    if (mode == "duda") { ov.BoxToggleDoubt(picked); Close2(); return; }
                    replaceTarget = picked; SetText(""); Refresh2(); return; // cambiar: ahora el nuevo
                }
                // perk o addon nuevo con la lista llena: elegir cual sale
                if (picked != null && (picked.K == Kind.Perk || picked.K == Kind.Addon) && ov.IsFull(picked) && mode.Length == 0)
                {
                    replaceNew = picked; replaceDoubt = doubt; SetText(""); Refresh2(); return;
                }
                var cur = ov.CurrentKiller;
                if (picked != null && picked.K == Kind.Killer && cur != null && cur != picked)
                {
                    confirmKiller = picked;
                    hint.Text = L("¿Cambiar ", "Change ") + cur.Name + L(" por ", " to ") + picked.Name + L("? Se borran los addons.\nEnter: sí, cambiar · Esc: no", "? This clears the add-ons.\nEnter: yes, change · Esc: no");
                    hint.ForeColor = Color.FromArgb(240, 170, 80);
                    return;
                }
                ov.SubmitTyped(tb.Text, picked, doubt, byArrows);
                tb.Text = ""; ov.HideTypeBox(); // elegido: se cierra (T para anotar otra cosa)
            }

            bool replaceDoubt;
            void Close2() { replaceTarget = replaceNew = null; tb.Text = ""; ov.HideTypeBox(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(Color.FromArgb(90, 90, 110))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        // ---- lo que entendio Whisper: se anota todo en logs\escuchado.txt y (opcional) se muestra un momento abajo del widget
        string heardLine = ""; int heardUntil;
        bool ShowHeard { get { return !Regex.IsMatch(cfg.Get("VerTranscripcion", "si"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }

        void Heard(string src, string text, string res, bool show)
        {
            try
            {
                string path = Path.Combine(dir, "logs", "escuchado.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  [" + src + "]  \"" + text.Trim() + "\"  ->  " + res + "\r\n", Encoding.UTF8);
            }
            catch { }
            if (!show || !ShowHeard) return;
            heardLine = "🎙 [" + src + "] “" + text.Trim() + "” → " + res;
            heardUntil = Environment.TickCount + 7000;
            Relayout();
        }

        void OnHandsHeard(string text, double secs, int ms)
        {
            string raw = text.Trim(), nt = Txt.Norm(text);
            var sb = new StringBuilder("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (SIN TECLA) ====\r\n");
            sb.Append("micrófono: " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s, Whisper " + ms + " ms\r\n");
            sb.Append("whisper: \"" + raw + "\"\r\n");
            var m = HandsRx.Match(nt);
            if (!m.Success) { sb.Append("resultado: (no es un comando: se ignora)"); Debug(sb.ToString()); Heard("sin tecla", raw, "no es un comando, se ignora", false); return; }
            if (raw.StartsWith("¿") || raw.EndsWith("?")) { sb.Append("resultado: (es una pregunta: se ignora)"); Debug(sb.ToString()); Heard("sin tecla", raw, "es una pregunta, se ignora", false); return; }
            if (Regex.IsMatch(nt.Substring(0, m.Index), @"\b(no|ni|nunca|tampoco)\s*$")) { sb.Append("resultado: (frase negativa: se ignora)"); Debug(sb.ToString()); Heard("sin tecla", raw, "frase negativa, se ignora", false); return; }
            sb.Append("antes:   " + st.Describe() + "\r\n");
            var before = st.Snapshot();
            st.Trace.Clear();
            string what = st.Apply(m.Value);
            st.ForgetGuess(); // sin tecla no se aprenden apodos ni se pregunta "¿X?"
            foreach (var line in st.Trace) sb.Append(line + "\r\n");
            sb.Append("resultado: " + what + "\r\ndespues: " + st.Describe());
            Debug(sb.ToString());
            Log("[sin tecla] " + raw, what);
            if (Lang.Fail(what) || Lang.Already(what)) { Heard("sin tecla", raw, what, false); return; } // sin anotar: solo se ve si "mostrar lo que entiende" esta prendido
            st.Save(Path.Combine(dir, "estado.txt"));
            PublishDiff(before);
            Feedback("🎙 “" + raw + "” → " + what, Color.FromArgb(150, 220, 150), 6000);
        }

        void OnHeard(string text, double secs, int ms)
        {
            string nt = Txt.Norm(text);
            var m = HeardRx.Match(nt);
            var sb = new StringBuilder("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (DISCORD) ====\r\n");
            sb.Append("audio de la PC: " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s, Whisper " + ms + " ms\r\n");
            sb.Append("whisper: \"" + text.Trim() + "\"\r\n");
            if (!m.Success) { sb.Append("resultado: (no es un \"tiene…\": se ignora)"); Debug(sb.ToString()); Heard("discord", text, "se ignora", false); return; }
            sb.Append("antes:   " + st.Describe() + "\r\n");
            var before = st.Snapshot();
            st.Trace.Clear();
            string what = st.Apply("creo que " + m.Value); // lo que dicen otros entra como sospecha
            st.ForgetGuess();
            foreach (var line in st.Trace) sb.Append(line + "\r\n");
            sb.Append("resultado: " + what + "\r\ndespues: " + st.Describe());
            Debug(sb.ToString());
            Log("[discord] " + text, what);
            if (Lang.Fail(what) || Lang.Already(what)) return; // no molestar con lo que no sirvio
            st.Save(Path.Combine(dir, "estado.txt"));
            PublishDiff(before);
            Feedback("Discord: " + what, Color.FromArgb(130, 190, 255), 6000);
        }

        // ---- sala compartida (ntfy.sh): cada cambio se manda como diferencia y todas las PCs lo aplican en el mismo orden
        Room room;
        string roomStatus = "";
        ToolStripMenuItem roomLabel, roomCopy, roomLeave, autoItem;

        string MyName { get { return cfg.Get("Nombre", ""); } }

        // configuraciones creadas por versiones viejas: lo que seguia con el valor de fabrica viejo pasa al nuevo
        // (lo que el usuario cambio a mano no se toca). Se hace una sola vez.
        void MigrateConfig()
        {
            if (cfg.GetInt("ConfigVersion", 0) >= 2) return;
            var changes = new[] {
                Tuple.Create("Tecla", "Alt", "Shift+V"),
                Tuple.Create("TeclaLimpiar", "G", "Shift+G"),
                Tuple.Create("TeclaAyuda", "H", "Shift+H"),
                Tuple.Create("TeclaNombres", "", "Shift+M"),
                Tuple.Create("TeclaEscribir", "Ctrl+T", "Shift+T"),
                Tuple.Create("TamanoPerk", "48", "72"),
                Tuple.Create("TamanoKiller", "10", "12"),
            };
            foreach (var c in changes)
            {
                string v;
                if (cfg.V.TryGetValue(c.Item1, out v) && string.Equals(v.Trim(), c.Item2, StringComparison.OrdinalIgnoreCase))
                    SetCfg(c.Item1, c.Item3);
            }
            SetCfg("ConfigVersion", "2");
        }

        void SetCfg(string key, string value)
        {
            cfg.V[key] = value;
            Cfg.Set(Path.Combine(dir, "config.ini"), key, value);
        }

        // ---- sala automatica por Steam: todos los del mismo grupo (lobby de Steam de DBD) caen en el mismo canal
        bool AutoRoom { get { return !Regex.IsMatch(cfg.Get("SalaAuto", "si"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }
        bool ManualRoom { get { return Room.CleanCode(cfg.Get("Sala", "")).Length >= 6; } }
        Process steamProc;
        string steamLobby = "", steamMe = "", steamErr = "";
        Dictionary<string, string> partyNames = new Dictionary<string, string>(); // huella -> nombre de Steam (solo amigos)
        int partyCount;


        static string Sha(string s)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s)).Select(b => b.ToString("x2")));
        }
        string Fingerprint(string sid) { return Sha("dbdov|" + steamLobby + "|" + sid).Substring(0, 12); } // no viaja el SteamID
        string RoomTopic()
        {
            if (ManualRoom) return Room.TopicFor(Room.CleanCode(cfg.Get("Sala", "")));
            if (AutoRoom && steamLobby.Length > 0) return "dbdov-g-" + Sha("dbdov-grupo|" + steamLobby).Substring(0, 24);
            return null;
        }

        void SteamTick()
        {
            bool want = AutoRoom && !ManualRoom && SteamDbd.GameRunning();
            if (want && steamProc == null)
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, "--steam-watch " + Process.GetCurrentProcess().Id)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                    var p = Process.Start(psi);
                    Job.Add(p);
                    steamProc = p;
                    DebugEvent("steam: vigía iniciado (DBD abierto)");
                    new Thread(() =>
                    {
                        string line;
                        try { while ((line = p.StandardOutput.ReadLine()) != null) { string l = line; BeginInvoke((Action)(() => OnSteamLine(l))); } } catch { }
                        try { BeginInvoke((Action)(() => { if (steamProc == p) { steamProc = null; SetLobby(""); DebugEvent("steam: vigía terminó"); } })); } catch { }
                    }) { IsBackground = true }.Start();
                }
                catch (Exception ex) { steamErr = ex.Message; DebugEvent("steam: no pude iniciar el vigía: " + ex.Message); }
            }
            else if (!want && steamProc != null)
            {
                try { steamProc.Kill(); } catch { }
                steamProc = null;
                SetLobby("");
            }
        }

        void OnSteamLine(string line)
        {
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(line); } catch { return; }
            object o;
            if (d.TryGetValue("error", out o)) { steamErr = (o ?? "").ToString(); DebugEvent("steam: " + steamErr); UpdateRoomMenu(); return; }
            steamErr = "";
            steamMe = d.TryGetValue("me", out o) ? (o ?? "").ToString() : "";
            string lobby = d.TryGetValue("lobby", out o) ? (o ?? "").ToString() : "";
            var party = d.TryGetValue("party", out o) ? o as Dictionary<string, object> : null;
            bool changed = lobby != steamLobby;
            steamLobby = lobby; // antes de calcular huellas (dependen del lobby)
            var oldNames = new HashSet<string>(partyNames.Values.Where(n => n.Length > 0));
            int oldCount = partyCount;
            partyNames = new Dictionary<string, string>();
            partyCount = party != null ? party.Count : 0;
            if (party != null) foreach (var kv in party) partyNames[Fingerprint(kv.Key)] = (kv.Value ?? "").ToString();
            // aviso corto (no queda nada fijo en el widget): quien entro o salio del grupo
            if (!changed || oldCount > 0)
            {
                var nowNames = new HashSet<string>(partyNames.Values.Where(n => n.Length > 0));
                var joined = nowNames.Where(n => !oldNames.Contains(n)).ToList();
                var left = oldNames.Where(n => !nowNames.Contains(n)).ToList();
                if (joined.Count > 0 || left.Count > 0)
                    Feedback((joined.Count > 0 ? "👥 " + string.Join(", ", joined) + L(" en el grupo", " joined the group") : "") +
                             (left.Count > 0 ? (joined.Count > 0 ? " · " : "👥 ") + string.Join(", ", left) + L(" salió", " left") : "") +
                             (partyCount == 0 ? L(" · solo vos", " · just you") : ""), Color.FromArgb(130, 200, 140), 5000);
            }
            DebugEvent("steam: lobby " + (lobby.Length > 0 ? Sha(lobby).Substring(0, 8) : "(ninguno)") + " · amigos en el grupo: " + partyCount);
            if (changed) StartRoom(); else UpdateRoomMenu();
        }

        void SetLobby(string lobby)
        {
            if (lobby == steamLobby) return;
            steamLobby = lobby; partyNames = new Dictionary<string, string>(); partyCount = 0;
            if (!ManualRoom) StartRoom();
        }

        void StartRoom()
        {
            if (room != null) { room.Stop(); room = null; }
            roomStatus = "";
            string topic = RoomTopic();
            if (topic == null) { UpdateRoomMenu(); return; }
            var r = new Room();
            r.OnOp = op => BeginInvoke((Action)(() => { if (room == r) OnRoomOp(op, r); }));
            r.OnStatus = s => BeginInvoke((Action)(() =>
            {
                if (room != r || s == roomStatus) return;
                roomStatus = s; UpdateRoomMenu();
                DebugEvent("sala: " + s);
            }));
            room = r;
            roomMembers.Clear();
            joinedAt = UnixNow();
            joinedWithList = st.HasAny();
            r.Start(topic);
            if (ManualRoom) r.Publish(Stamp(new Dictionary<string, object> { { "join", true }, { "t", UnixNow() } }));
            PublishSync(true);
            DebugEvent("sala: entrando a " + (ManualRoom ? "la sala " + cfg.Get("Sala", "") : "la del grupo de Steam") + (MyName.Length > 0 ? " como " + MyName : ""));
            UpdateRoomMenu();
        }

        // quien mando un cambio: nombre propio si lo puso, si no el nombre de Steam del amigo (resuelto en esta PC)
        string SenderName(Dictionary<string, object> op)
        {
            object o; string n;
            if (op.TryGetValue("name", out o) && (n = (o ?? "").ToString()).Length > 0) return n;
            if (op.TryGetValue("h", out o) && partyNames.TryGetValue((o ?? "").ToString(), out n) && n.Length > 0) return n;
            return L("alguien del grupo", "someone in the group");
        }

        // ---- maximo 4 por sala con codigo: cada PC tiene un id al azar; todos ven los avisos de entrada/salida
        // en el mismo orden (ntfy los ordena), asi que todos coinciden en quienes son los 4 primeros.
        const int MaxRoom = 4;
        readonly List<string> roomMembers = new List<string>();
        string InstallId
        {
            get
            {
                string id = cfg.Get("IdLocal", "");
                if (id.Length == 0) { id = Guid.NewGuid().ToString("N").Substring(0, 10); SetCfg("IdLocal", id); }
                return id;
            }
        }
        static long UnixNow() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }
        int RoomCount { get { return Math.Min(roomMembers.Count, MaxRoom); } }

        // al salir de una sala con codigo: avisar (asi libera el lugar)
        void AnnounceLeave()
        {
            if (room == null || !ManualRoom) return;
            Room.PublishNow(room.Topic, Stamp(new Dictionary<string, object> { { "leave", true }, { "t", UnixNow() }, { "from", room.Me } }));
        }

        // devuelve true si el aviso era de entrada/salida (no hay nada que anotar)
        bool RoomMembership(Dictionary<string, object> op)
        {
            if (!ManualRoom) return false;
            object w, tt;
            op.TryGetValue("who", out w);
            string who = (w ?? "").ToString();
            bool join = op.ContainsKey("join"), leave = op.ContainsKey("leave");
            if (!join && !leave) return false;
            if (who.Length == 0) return true;
            long t = op.TryGetValue("t", out tt) ? Convert.ToInt64(tt) : 0;
            bool recent = UnixNow() - t < 120; // los avisos viejos (historial de la sala) no se muestran
            bool me = who == InstallId;
            string nm = SenderName(op);
            if (join && !roomMembers.Contains(who))
            {
                roomMembers.Add(who);
                int pos = roomMembers.IndexOf(who);
                if (me && pos >= MaxRoom)
                {
                    DebugEvent("sala: llena (" + MaxRoom + "/" + MaxRoom + "), salgo");
                    LeaveRoom();
                    Feedback(L("La sala está llena (", "The room is full (") + MaxRoom + "/" + MaxRoom + L("): no pudiste entrar", "): you couldn't join"), Color.FromArgb(240, 170, 80), 8000);
                    return true;
                }
                if (!me && recent && pos < MaxRoom) Feedback(nm + L(" entró a la sala (", " joined the room (") + RoomCount + "/" + MaxRoom + ")", Color.FromArgb(130, 190, 255), 5000);
            }
            else if (leave && roomMembers.Remove(who))
            {
                if (!me && recent) Feedback(nm + L(" salió de la sala (", " left the room (") + RoomCount + "/" + MaxRoom + ")", Color.Silver, 5000);
            }
            Relayout();
            return true;
        }

        Dictionary<string, object> Stamp(Dictionary<string, object> op)
        {
            op["name"] = MyName;
            if (!op.ContainsKey("t")) op["t"] = UnixNow();
            if (ManualRoom) op["who"] = InstallId;
            if (!ManualRoom && steamMe.Length > 0 && steamLobby.Length > 0) op["h"] = Fingerprint(steamMe);
            return op;
        }

        void OnRoomOp(Dictionary<string, object> op, Room r)
        {
            object from, name, h;
            op.TryGetValue("from", out from);
            op.TryGetValue("name", out name);
            op.TryGetValue("h", out h);
            if (RoomMembership(op)) return;
            // sala con codigo: lo que manda el 5to en adelante no se anota
            object wo;
            if (ManualRoom && op.TryGetValue("who", out wo) && wo != null && roomMembers.Contains(wo.ToString()) && roomMembers.IndexOf(wo.ToString()) >= MaxRoom) return;
            bool mine = (from ?? "").ToString() == r.Me
                     || (steamMe.Length > 0 && steamLobby.Length > 0 && (h ?? "").ToString() == Fingerprint(steamMe))
                     || (MyName.Length > 0 && (name ?? "").ToString() == MyName);
            if (HandleSync(op, mine)) return;
            object rt0;
            if (!mine && op.ContainsKey("reset") && op.TryGetValue("t", out rt0) && UnixNow() - Convert.ToInt64(rt0) < 120) { RecordMatch(); HideMap(false); }
            object mk;
            if (op.TryGetValue("mark", out mk))
            {
                object mn0;
                if (!mine && op.TryGetValue("t", out rt0) && UnixNow() - Convert.ToInt64(rt0) < 600 && op.TryGetValue("mapn", out mn0)
                    && mapShown != null && (mn0 ?? "").ToString() == mapShown && mapWin != null && MapGuides.ContainsKey(mapShown))
                {
                    string mwho = SenderName(op), v = (mk ?? "").ToString();
                    var xy = v.Split(',');
                    int x, y;
                    if (xy.Length == 2 && int.TryParse(xy[0], out x) && int.TryParse(xy[1], out y))
                    {
                        mapWin.SetMark(mwho, new PointF(x, y));
                        if (MapMarksOn) Feedback(mwho + L(" marcó: ", " marked: ") + DescribeSpot(MapGuides[mapShown], x, y), Color.FromArgb(255, 150, 150), 6000);
                    }
                    else mapWin.SetMark(mwho, null);
                }
                return;
            }
            object mp;
            if (op.TryGetValue("map", out mp))
            {
                // mapa elegido por alguien de la sala (los de hace mas de 10 min, del historial, no)
                if (!mine && op.TryGetValue("t", out rt0) && UnixNow() - Convert.ToInt64(rt0) < 600)
                {
                    string mn = (mp ?? "").ToString();
                    if (mn.Length == 0) HideMap(false); else { ShowMap(mn, false); Feedback(SenderName(op) + L(": mapa ", ": map ") + mn, Color.FromArgb(130, 190, 255), 5000); }
                }
                return;
            }
            string what = st.ApplyRemote(op);
            if (what.Length == 0) return; // ya estaba aplicado (por ejemplo el eco de lo propio)
            object ct;
            if (op.TryGetValue("t", out ct)) st.LastChange = Math.Max(st.LastChange, Convert.ToInt64(ct));
            st.Save(Path.Combine(dir, "estado.txt"));
            string who = SenderName(op);
            DebugEvent("sala: " + (mine ? "(propio) " : who + ": ") + what);
            if (!mine) Feedback(who + ": " + what, Color.FromArgb(130, 190, 255), 5000);
            else Relayout();
        }

        void PublishDiff(object before)
        {
            if (before != null) st.LastChange = UnixNow();
            CountPerkUse(before as State.Snap);
            if (room == null || before == null) return;
            var op = st.Diff(before);
            if (op == null) return;
            room.Publish(Stamp(op));
        }

        // ---- perks mas anotados (frecuentes.txt: "cantidad<TAB>nombre"), para la primera rueda de Perk
        Dictionary<string, int> perkUse;
        Dictionary<string, int> PerkUse()
        {
            if (perkUse != null) return perkUse;
            perkUse = new Dictionary<string, int>();
            try
            {
                string p = Path.Combine(dir, "frecuentes.txt");
                if (File.Exists(p))
                    foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        var f = line.Split('\t'); int n;
                        if (f.Length == 2 && int.TryParse(f[0], out n)) perkUse[f[1]] = n;
                    }
            }
            catch { }
            return perkUse;
        }
        void CountPerkUse(State.Snap before)
        {
            if (before == null || before.Perks == null) return;
            var added = st.Perks.Where(p => !before.Perks.Contains(p)).Select(p => p.Name).ToList();
            if (st.Killer != null && st.Killer != before.Killer) added.Add("K:" + st.Killer.Name); // killers: misma lista, con "K:"
            if (added.Count == 0) return;
            var u = PerkUse();
            foreach (var p in added) { int n; u.TryGetValue(p, out n); u[p] = n + 1; }
            try { File.WriteAllLines(Path.Combine(dir, "frecuentes.txt"), u.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + "\t" + kv.Key), Encoding.UTF8); } catch { }
        }
        // perks de killer mas usados por la comunidad, de mas a menos. Fijo (sin consultar internet): el top 5 es el oficial
        // de Behaviour (stats del 18/10/2026); el resto, el meta conocido. Actualizarlo a mano con cada version.
        static readonly string[] CommonPerks = {
            "Pain Resonance", "Nowhere to Hide", "Pop Goes the Weasel", "Lethal Pursuer", "Barbecue", "Dead Man", "Bamboozle", "Deadlock",
            "Corrupt Intervention", "Lightborn", "Hex: Ruin", "Grim Embrace", "No Way Out", "No One Escapes Death", "Eruption", "Sloppy Butcher",
            "Discordance", "Surveillance", "Agitation", "Starstruck", "Hex: Plaything", "Hex: Pentimento", "Gift of Pain", "Floods of Rage",
            "Hex: Undying", "Hex: Devour Hope", "Coup de Gr", "Infectious Fright", "Save the best for last", "Enduring", "Spirit Fury",
            "Brutal Strength", "Fire Up", "Oppression", "Overcharge", "Call of Brine", "Thanatophobia", "Hex: The Third Seal", "Hex: Crowd Control",
            "Leverage", "Hysteria", "Unrelenting", "Rapid Brutality", "Machine Learning", "Dark Devotion", "Ultimate Weapon", "Friends",
            "Darkness Revealed", "Weave Attunement", "Batteries Included", "Merciless Storm", "Hex: Haunted Ground", "Hex: Two Can Play",
            "Hex: Face the Darkness", "Hex: Thrill of the Hunt", "Hex: Wretched Fate", "Hex: Blood Favor", "Hex: Retribution", "Hex: Nothing But Misery",
            "Blood Warden", "Terminus", "Rancor", "Remember Me", "Bitter Murmur", "Haywire", "Iron Grasp", "Forever Entwined", "Gearhead",
            "Trail of Torment", "Dying Light", "Knock Out", "Distressing", "Coulrophobia", "Mad Grit", "Play With Your Food", "Furtive Chase" };
        // killers mas jugados (fijo, de mas a menos; no hay estadisticas publicas completas). Lo tuyo pesa mas.
        static readonly string[] CommonKillers = { "The Mastermind", "The Huntress", "The Blight", "The Nurse", "The Wraith", "The Spirit", "The Shape",
            "The Ghost Face", "The Trapper", "The Pig", "The Hillbilly", "The Xenomorph", "The Good Guy", "The Dark Lord", "The Houndmaster", "The Ghoul",
            "The Legion", "The Nightmare", "The Animatronic", "The Krasue", "The Slasher", "The First", "The Lich", "The Unknown", "The Doctor", "The Cannibal",
            "The Clown", "The Plague", "The Nemesis", "The Oni", "The Executioner", "The Demogorgon", "The Cenobite", "The Artist", "The Deathslinger",
            "The Trickster", "The Dredge", "The Onryō", "The Knight", "The Singularity", "The Twins", "The Hag", "The Skull Merchant", "The Judgment" };
        public int KillerScore(Entry k)
        {
            int n, i = Array.IndexOf(CommonKillers, k.Name);
            PerkUse().TryGetValue("K:" + k.Name, out n);
            return n * 1000 + (i < 0 ? 0 : CommonKillers.Length - i);
        }
        internal static readonly Dictionary<string, string> PortraitFix = new Dictionary<string, string> {
            { "The Hag", "K06_TheHag_Portrait.png" }, { "The Shape", "K05_TheShape_Portrait.png" }, { "The Ghost Face", "K16_TheGhostFace_Portrait.png" },
            { "The Mastermind", "K29_TheMastermind_Portrait.png" }, { "The Good Guy", "K34_TheGoodGuy_Portrait.png" }, { "The Dark Lord", "K37_TheDarkLord_Portrait.png" } };
        // foto de cada mapa (IconMap_<x>.png del wiki; los nombres no siguen ninguna regla, por eso a mano). Clave: Bare(nombre)
        internal static readonly Dictionary<string, string> MapPhoto = new[] {
            "Azarov's Resting Place=Jnk_Office", "Blood Lodge=Jnk_Lodge", "Gas Heaven=Jnk_GasStation", "Wreckers' Yard=Jnk_Scrapyard", "Wretched Shop=Jnk_Garage",
            "Preschool I=Eng_Elmstreet", "Preschool II=Eng_ElmstreetII", "Preschool III=Eng_ElmstreetIII", "Preschool IIIV=Eng_ElmstreetIV", "Preschool V=Eng_ElmstreetV",
            "Fractured Cowshed=Frm_Barn", "Rancid Abbatoir=Frm_Slaughterhouse", "Rotten Fields=Frm_Cornfield", "The Thompson House=Frm_Farmhouse", "Torment Creek=Frm_Silo",
            "Disturbed Ward=Asy_Asylum", "Father Campbells Chapel=Asy_Chapel", "Shattered Square=Qtm_Quantumlevel01", "Forgotten Ruins=Qtm_Churroslevel01",
            "Eyrie of Crows=Ion_Ionlevel01", "Dead Sands=Ice_Level01", "The Underground Complex=Qat_Laboratory",
            "Coal Tower=Ind_CoalTower", "Coal Tower II=Ind_CoalTower", "Groaning Storehouse=Ind_Storehouse", "Groaning Storehouse II=Ind_Storehouse",
            "Ironworks Of Misery=Ind_Foundry", "Ironworks Of Misery II=Ind_Foundry", "Shelter Woods=Ind_Forest", "Shelter Woods II=Ind_Forest",
            "Suffocation Pit=Ind_Mine", "Suffocation Pit II=Ind_Mine", "Mother's Dwelling=Brl_MadHouse", "Temple of Purgation=Brl_Temple",
            "Grim Pantry=Swp_GrimPantry", "The Pale Rose=Swp_ThePaleRose", "Family Residence=Hti_Manor", "Family Residence II=Hti_Manor",
            "Sanctum of Wrath=Hti_Shrine", "Sanctum of Wrath II=Hti_Shrine", "Police Station East Wing=Ecl_Eclipselevel01", "Police Station West Wing=Ecl_Orionlevel01",
            "Dead Dawg Saloon=Ukr_Saloon", "Garden of Joy=Meteor_Meteorlevel01", "Greenville Square=Meteor_Applepielevel01", "Fallen Refuge=Apl_Shack",
            "Freddy Fazbears Pizza=Apl_Level02", "The Game=Fin_TheGame", "Lampkin Lane=Sub_Street", "Midwich Elementary School=Wal_Level01",
            "Mount Ormond Resort=Kny_Cottage", "Mount Ormond Resort II=Kny_Cottage", "Mount Ormond Resort III=Kny_Cottage", "Ormond Lake Mine=Glo_Level01",
            "Treatment Theatre=Hos_Treatment", "Toba Landing=Uba_Umbralevel01", "Nostromo Wreckage=Uba_Wormholelevel01", "Trickster's Delusion=QuiLevel01" }
            .Select(x => x.Split('=')).ToDictionary(x => Bare(x[0]), x => x[1]);
        internal static string Bare(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD).ToLowerInvariant())
                if (c >= 'a' && c <= 'z') sb.Append(c); // sin tildes ni signos: "Coup de Grâce" = "coupdegrace"
            return sb.ToString();
        }
        Dictionary<Entry, int> commonRank;
        // puntaje para ordenar: cada vez que vos lo anotaste vale mas que cualquier puesto en la lista de la comunidad
        public int PerkScore(Entry p)
        {
            if (commonRank == null)
            {
                commonRank = new Dictionary<Entry, int>();
                for (int i = 0; i < CommonPerks.Length; i++)
                {
                    string b = Bare(CommonPerks[i]);
                    foreach (var e in cat.Perks.Where(x => Bare(x.Name).Contains(b)))
                        if (!commonRank.ContainsKey(e)) commonRank[e] = CommonPerks.Length - i;
                }
            }
            int n, r;
            PerkUse().TryGetValue(p.Name, out n);
            commonRank.TryGetValue(p, out r);
            return n * 1000 + r;
        }

        void PublishReset()
        {
            st.LastChange = UnixNow();
            if (room == null) return;
            room.Publish(Stamp(new Dictionary<string, object> { { "reset", true } }));
        }

        // ---- sincronizar al juntarse: cada uno manda su lista entera con la hora de su ultimo cambio y todos se
        // quedan con la mas nueva. Va como "borrar + agregar todo", asi las versiones viejas de la app tambien la toman.
        long joinedAt; bool joinedWithList;
        void PublishSync(bool hello)
        {
            if (room == null) return;
            var op = st.Diff(State.EmptySnapshot()) ?? new Dictionary<string, object>();
            op["reset"] = true; op["sync"] = true; op["lc"] = st.LastChange;
            if (hello) op["hello"] = true;
            room.Publish(Stamp(op));
        }

        // devuelve true si el mensaje ya quedo resuelto (no hay que aplicarlo como un cambio comun)
        bool HandleSync(Dictionary<string, object> op, bool mine)
        {
            object o;
            long t = op.TryGetValue("t", out o) ? Convert.ToInt64(o) : 0;
            if (!op.ContainsKey("sync"))
            {
                // lo viejo de la sala (de antes de entrar) solo se usa si llegue con la lista vacia;
                // si traia algo, decide la hora de cada lista
                return joinedWithList && t > 0 && t < joinedAt - 5;
            }
            if (mine) return true;
            long lc = op.TryGetValue("lc", out o) ? Convert.ToInt64(o) : 0;
            if (lc > st.LastChange)
            {
                string what = st.ApplyRemote(op);
                st.LastChange = lc;
                st.Save(Path.Combine(dir, "estado.txt"));
                DebugEvent("sala: me quedo con la lista de " + SenderName(op) + " (más nueva)" + (what.Length > 0 ? ": " + what : ""));
                Feedback(L("lista igualada con ", "list synced with ") + SenderName(op), Color.FromArgb(130, 190, 255), 4000);
            }
            else if (lc < st.LastChange && op.ContainsKey("hello") && UnixNow() - t < 120)
            {
                DebugEvent("sala: " + SenderName(op) + " entró con una lista más vieja: le mando la mía");
                PublishSync(false);
            }
            return true;
        }

        // estado completo (al crear una sala con codigo, para que el que entra vea lo que ya habia)
        void PublishAll()
        {
            if (room == null) return;
            PublishReset();
            var op = st.Diff(State.EmptySnapshot());
            if (op == null) return;
            room.Publish(Stamp(op));
        }

        bool AskName()
        {
            string n = Microsoft.VisualBasic.Interaction.InputBox(
                L("¿Con qué nombre te ven los demás cuando anotás algo?\n\n(En la sala automática de Steam, si lo dejás vacío tus amigos ven tu nombre de Steam, sin que se mande por internet.)",
                  "What name do the others see when you add something?\n\n(In the automatic Steam room, if you leave it empty your friends see your Steam name, without it being sent over the internet.)"),
                L("Sala compartida", "Shared room"), MyName).Trim();
            if (n.Length == 0) return MyName.Length > 0;
            SetCfg("Nombre", n.Length > 20 ? n.Substring(0, 20) : n);
            return true;
        }

        void CreateRoom()
        {
            if (MyName.Length == 0 && !AskName()) return;
            string code = Room.NewCode();
            AnnounceLeave();
            SetCfg("Sala", code);
            StopSteam();
            StartRoom();
            PublishAll();
            try { Clipboard.SetText(L("sala ", "room ") + code); } catch { }
            MessageBox.Show(Lang.En
                ? "Room created. Code: " + code + "\n\n(already copied as \"room " + code + "\": paste it in the chat)\n\n" +
                  "The others: " + TypeKeyText + " and type \"room " + code + "\" (or Settings → Room → Join…).\n\n" +
                  "While you use a code, the automatic Steam room is paused."
                : "Sala creada. Código: " + code + "\n\n(ya está copiado como \"sala " + code + "\": pegalo en el chat)\n\n" +
                  "Los demás: " + TypeKeyText + " y escriben \"sala " + code + "\" (o Configuración → Sala → Unirse…).\n\n" +
                  "Mientras uses código, la sala automática de Steam queda en pausa.",
                            L("Sala compartida", "Shared room"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void JoinRoom()
        {
            string code = Room.CleanCode(Microsoft.VisualBasic.Interaction.InputBox(
                L("Pegá el código de la sala que te pasó tu amigo:", "Paste the room code your friend gave you:"), L("Unirse con código", "Join with code"), ""));
            if (code.Length == 0) return;
            if (code.Length < 6) { MessageBox.Show(L("Ese código no parece válido.", "That code doesn't look valid."), L("Sala compartida", "Shared room")); return; }
            if (MyName.Length == 0 && !AskName()) return;
            JoinRoomCode(code);
        }

        // ---- sala desde el cuadro de escribir (sin salir del juego): "sala nueva", "sala <codigo>", "sala salir"
        public string BoxRoomHint(string text)
        {
            var m = Regex.Match(Txt.Norm(text ?? ""), @"^(?:sala|room)\b\s*(.*)$");
            if (!m.Success) return null;
            string r = m.Groups[1].Value.Trim();
            if (r.Length == 0) return Lang.En ? (ManualRoom ? "You're in room " + cfg.Get("Sala", "") + " · \"room leave\" · \"room new\"" : "\"room new\": create · \"room CODE\": join · \"room leave\"")
                : ManualRoom ? "Estás en la sala " + cfg.Get("Sala", "") + " · \"sala salir\" · \"sala nueva\"" : "\"sala nueva\": crear · \"sala CÓDIGO\": entrar · \"sala salir\"";
            if (Regex.IsMatch(r, @"^(nueva|crear|crea|new)$")) return L("Enter: crear sala (el código queda copiado para el chat)", "Enter: create room (the code gets copied for the chat)");
            if (Regex.IsMatch(r, @"^(salir|dejar|leave|exit)$")) return L("Enter: salir de la sala con código", "Enter: leave the code room");
            string code = Room.CleanCode(r);
            return code.Length >= 6 ? L("Enter: entrar a la sala ", "Enter: join room ") + code : L("Código incompleto", "Incomplete code");
        }
        public bool BoxRoom(string text)
        {
            var m = Regex.Match(Txt.Norm(text ?? ""), @"^(?:sala|room)\b\s*(.*)$");
            if (!m.Success) return false;
            string r = m.Groups[1].Value.Trim();
            if (MyName.Length == 0) SetCfg("Nombre", "Survivor " + new Random().Next(10, 100)); // sin ventana para preguntar (se cambia desde el menu)
            if (Regex.IsMatch(r, @"^(nueva|crear|crea|new)$"))
            {
                string code = Room.NewCode();
                AnnounceLeave();
                SetCfg("Sala", code);
                StopSteam(); StartRoom(); PublishAll();
                try { Clipboard.SetText(L("sala ", "room ") + code); } catch { }
                Feedback(Lang.En ? "room created: " + code + " · copied \"room " + code + "\", paste it in the chat (Ctrl+V)" : "sala creada: " + code + " · copiado \"sala " + code + "\", pegalo en el chat (Ctrl+V)", Color.FromArgb(130, 190, 255), 15000);
                return true;
            }
            if (Regex.IsMatch(r, @"^(salir|dejar|leave)$")) { if (ManualRoom) LeaveRoom(); return true; }
            string c = Room.CleanCode(r);
            if (c.Length < 6) return false;
            JoinRoomCode(c);
            return true;
        }

        void JoinRoomCode(string code)
        {
            AnnounceLeave();
            st.Clear(); // lo que hay en la sala reemplaza lo local
            st.Save(Path.Combine(dir, "estado.txt"));
            SetCfg("Sala", code);
            StopSteam();
            StartRoom();
            Feedback(L("entraste a la sala ", "you joined room ") + code, Color.FromArgb(130, 190, 255), 5000);
        }

        void LeaveRoom()
        {
            AnnounceLeave();
            SetCfg("Sala", "");
            DebugEvent("sala: salí de la sala con código");
            StartRoom(); // si corresponde, vuelve la automatica (el vigia arranca en el proximo tick)
            SteamTick();
            Feedback(L("saliste de la sala con código", "you left the code room") + (AutoRoom ? L(" · vuelve la automática de Steam", " · back to the automatic Steam room") : ""), Color.Silver, 5000);
        }

        void StopSteam()
        {
            if (steamProc != null) { try { steamProc.Kill(); } catch { } steamProc = null; }
            steamLobby = ""; partyNames = new Dictionary<string, string>(); partyCount = 0;
        }

        void SetAuto(bool v)
        {
            SetCfg("SalaAuto", v ? "si" : "no");
            if (!v) { StopSteam(); if (!ManualRoom) StartRoom(); }
            else SteamTick();
            DebugEvent("sala automática de Steam: " + (v ? "activada" : "desactivada"));
            UpdateRoomMenu();
        }

        string RoomText()
        {
            string conn = room == null ? "" : " · " + (roomStatus.Length > 0 ? roomStatus : L("conectando…", "connecting…"));
            if (ManualRoom) return L("Sala con código ", "Code room ") + cfg.Get("Sala", "") + conn;
            if (!AutoRoom) return L("Sin sala", "No room");
            if (steamErr.Length > 0) return "Steam: " + steamErr;
            if (steamProc == null) return L("Automática: esperando que abras DBD", "Automatic: waiting for DBD to open");
            if (steamLobby.Length == 0) return L("Automática: esperando grupo de Steam", "Automatic: waiting for a Steam party");
            return L("Grupo de Steam", "Steam party") + (partyCount > 0 ? " (+" + partyCount + L(" amigo", " friend") + (partyCount > 1 ? "s" : "") + ")" : L(" (solo vos)", " (just you)")) + conn;
        }

        public bool previewRoomLine; // solo para probar en la vista
        bool ShowRoomLine { get { return !Regex.IsMatch(cfg.Get("MostrarSala", "si"), "^(no|n|false|0)$", RegexOptions.IgnoreCase); } }
        ToolStripMenuItem roomLineItem;
        bool roomHover; Rectangle roomRect; // icono de sala: se expande con el mouse encima
        string RoomLine()
        {
            if (!ShowRoomLine || (Preview && !previewRoomLine) || (room == null && !Preview)) return null;
            if (!ManualRoom && partyCount <= 0 && !Preview) return null;
            if (!roomHover) return "👥";
            string conn = roomStatus == Lang.L("conectado", "connected") ? "" : Lang.L(" · conectando…", " · connecting…");
            if (ManualRoom) return L("👥 sala ", "👥 room ") + cfg.Get("Sala", "") + (roomMembers.Count > 0 ? " · " + RoomCount + "/" + MaxRoom : "") + conn;
            if (partyCount > 0) return L("👥 grupo de Steam +", "👥 Steam party +") + partyCount + conn;
            return null;
        }

        void UpdateRoomMenu()
        {
            if (roomLabel == null) return;
            roomLabel.Text = RoomText();
            autoItem.Checked = AutoRoom;
            roomCopy.Enabled = roomLeave.Enabled = ManualRoom;
            string t = AppName + " v" + Updater.AppVersion + " · " + RoomText();
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            // el icono de la barra de tareas se pone verde mientras compartis la lista con alguien
            var ic = InGroup ? trayGreen : trayRed;
            if (ic != null && tray.Icon != ic) tray.Icon = ic;
            Relayout();
        }

        // en grupo = conectado a la sala y con alguien mas (grupo de Steam con amigos, o sala con codigo)
        bool InGroup { get { return room != null && (ManualRoom || partyCount > 0); } }
        Icon trayRed, trayGreen;

        // icono: un rombo como los perks, con un gancho adentro. Rojo = sin grupo, verde = compartiendo con alguien
        public static Bitmap Logo(int s, Color acc)
        {
            var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float c = s / 2f, r = s * 0.49f;
                var dia = new[] { new PointF(c, c - r), new PointF(c + r, c), new PointF(c, c + r), new PointF(c - r, c) };
                Color dark = Color.FromArgb(acc.R / 4, acc.G / 4, acc.B / 4);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(0, -1, s, s + 2), acc, dark, 90f)) g.FillPolygon(br, dia);
                // borde oscuro y un filo claro adentro (solo en los grandes)
                using (var pen = new Pen(Color.FromArgb(230, 10, 6, 8), Math.Max(1f, s / 24f))) g.DrawPolygon(pen, dia);
                if (s >= 32)
                {
                    float r2 = r * 0.84f;
                    using (var pen = new Pen(Color.FromArgb(90, 255, 220, 215), s / 64f))
                        g.DrawPolygon(pen, new[] { new PointF(c, c - r2), new PointF(c + r2, c), new PointF(c, c + r2), new PointF(c - r2, c) });
                }
                // el gancho: argolla arriba, palo, curva y punta afilada hacia arriba
                float w = Math.Max(1.5f, s * 0.07f);
                var hook = Color.FromArgb(245, 240, 236);
                using (var pen = new Pen(hook, w) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Flat })
                using (var br = new SolidBrush(hook))
                {
                    float sx = s * 0.56f, er = s * 0.055f, eyeY = s * 0.22f, mid = s * 0.57f, hr = s * 0.12f;
                    if (s >= 24) g.DrawEllipse(pen, sx - er, eyeY - er, 2 * er, 2 * er);
                    g.DrawLine(pen, sx, s >= 24 ? eyeY + er : s * 0.2f, sx, mid);
                    g.DrawArc(pen, sx - 2 * hr, mid - hr, 2 * hr, 2 * hr, 0f, 180f);
                    float tx = sx - 2 * hr;
                    g.FillPolygon(br, new[] { new PointF(tx - w / 2, mid + 0.5f), new PointF(tx + w / 2, mid + 0.5f), new PointF(tx + w * 0.35f, mid - s * 0.12f) });
                }
            }
            return bmp;
        }

        static Icon TrayIcon(Color bg)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = Logo(s, bg)) return Icon.FromHandle(bmp.GetHicon());
        }

        // --icono archivo.ico : el icono del .exe (varios tamaños, PNG adentro) con el mismo dibujo
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = sizes.Select(sz => { using (var b = Logo(sz, Color.FromArgb(190, 22, 34))) using (var ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); } }).ToList();
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off);
                    off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        // ---- ventana de configuracion: solo existe mientras esta abierta (cerrada no ocupa nada)
        SettingsWindow settingsWin;
        public void OpenSettings(string page)
        {
            if (settingsWin != null && !settingsWin.IsDisposed) { if (page != null) settingsWin.ShowPage(page); settingsWin.Activate(); return; }
            settingsWin = new SettingsWindow(this, page);
            settingsWin.FormClosed += (s, e) => { FlushCfg(); EndSample(); settingsWin = null; Relayout(); };
            settingsWin.Show();
            settingsWin.Activate();
        }

        // las barras cambian la config en vivo; el archivo se escribe al soltar la barra o al cerrar la ventana
        readonly HashSet<string> cfgDirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Live(string k, string v) { cfg.V[k] = v; cfgDirty.Add(k); }
        void FlushCfg()
        {
            foreach (var k in cfgDirty) Cfg.Set(Path.Combine(dir, "config.ini"), k, cfg.V[k]);
            cfgDirty.Clear();
        }

        // el mapa abierto, con el tamaño / lugar / opacidad de ahora (sin anotarlo en el log)
        void RefreshMap()
        {
            List<string> files;
            if (mapWin == null || mapShown == null || !cat.MapFiles.TryGetValue(mapShown, out files)) return;
            var e = cat.Maps.FirstOrDefault(m => m.Name == mapShown);
            mapWin.Set(mapShown, e != null ? e.ActionTag : "", files, MapSize);
            mapWin.Place(cfg.GetInt("MapaX", 20), cfg.GetInt("MapaY", 20));
            mapWin.Opacity = SafeOp(MapOpacity / 100.0);
            mapWin.Invalidate();
        }

        // lista de ejemplo (solo con la lista vacia) para ver como queda mientras se configura
        List<Entry> sampleItems; Entry sampleKiller;
        bool SampleOn { get { return sampleItems != null; } }
        void StartSample()
        {
            if (SampleOn || HasAnything()) return;
            var k = cat.Killers.FirstOrDefault(x => cat.Perks.Count(p => p.Owner == x.Name) >= 3 && cat.Addons.Count(a => a.Killer == x.Name) >= 2);
            if (k == null) return;
            var perks = cat.Perks.Where(p => p.Owner == k.Name).Take(3).Concat(cat.Perks.Where(p => string.IsNullOrEmpty(p.Owner)).Take(1)).ToList();
            var addons = cat.Addons.Where(a => a.Killer == k.Name).Take(2).ToList();
            sampleKiller = k;
            sampleItems = perks.Concat(addons).ToList();
            st.Killer = k; st.Perks.AddRange(perks); st.Addons.AddRange(addons);
            st.Offering = cat.Offerings.FirstOrDefault();
            if (st.Offering != null) sampleItems.Add(st.Offering);
            Relayout();
        }
        void EndSample()
        {
            if (!SampleOn) return;
            var s = sampleItems; sampleItems = null;
            st.Perks.RemoveAll(s.Contains); st.Addons.RemoveAll(s.Contains);
            st.Doubt.RemoveAll(s.Contains);
            if (st.Offering != null && s.Contains(st.Offering)) st.Offering = null;
            if (st.Killer == sampleKiller && st.Perks.Count == 0 && st.Addons.Count == 0) st.Killer = null;
            Relayout();
        }

        static readonly Tuple<string, string>[] Corners = {
            Tuple.Create("arriba-derecha", L("Arriba a la derecha", "Top right")), Tuple.Create("arriba-izquierda", L("Arriba a la izquierda", "Top left")),
            Tuple.Create("abajo-derecha", L("Abajo a la derecha", "Bottom right")), Tuple.Create("abajo-izquierda", L("Abajo a la izquierda", "Bottom left")) };

        // valores de fabrica de todo lo que es apariencia (las teclas, la sala y la voz no se tocan)
        static readonly Tuple<string, string>[] LookDefaults = {
            Tuple.Create("Posicion", "arriba-derecha"), Tuple.Create("MargenX", "30"), Tuple.Create("MargenY", "140"),
            Tuple.Create("Escala", "100"), Tuple.Create("TamanoPerk", "72"), Tuple.Create("TamanoLetra", "14"),
            Tuple.Create("TamanoKiller", "12"), Tuple.Create("Opacidad", "0.85"), Tuple.Create("TamanoMapa", "220"),
            Tuple.Create("OpacidadMapa", "90"), Tuple.Create("MapaX", "20"), Tuple.Create("MapaY", "20"), Tuple.Create("TamanoBotonBuild", "60") };

        // ---- estilo de la ventana: casi negro, rojo sangre, titulos condensados en mayusculas y rombos como los perks
        static class Dbd
        {
            public static readonly Color Bg = Color.FromArgb(14, 12, 14), Side = Color.FromArgb(8, 7, 9), Fg = Color.FromArgb(222, 216, 210),
                Dim = Color.FromArgb(138, 128, 126), Off = Color.FromArgb(78, 72, 72), Ctl = Color.FromArgb(26, 20, 22), CtlHi = Color.FromArgb(58, 18, 22),
                Acc = Color.FromArgb(150, 16, 26), AccHi = Color.FromArgb(214, 32, 44), Line = Color.FromArgb(52, 40, 42), Track = Color.FromArgb(40, 32, 34);
            static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
            // letra condensada de los titulos (Bahnschrift viene con Windows 10/11); si no esta, Segoe UI
            public static Font Head(float size) { return Get(new[] { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiBold", "Segoe UI Semibold" }, size); }
            public static Font Cond(float size) { return Get(new[] { "Bahnschrift SemiCondensed", "Bahnschrift", "Segoe UI" }, size); }
            static Font Get(string[] names, float size)
            {
                string key = names[0] + size;
                Font f;
                if (cache.TryGetValue(key, out f)) return f;
                foreach (var n in names)
                {
                    f = new Font(n, size);
                    if (f.Name == n) break;
                    f.Dispose(); f = null;
                }
                if (f == null) f = new Font("Segoe UI", size);
                cache[key] = f;
                return f;
            }
            public static PointF[] Diamond(float cx, float cy, float r) { return new[] { new PointF(cx, cy - r), new PointF(cx + r, cy), new PointF(cx, cy + r), new PointF(cx - r, cy) }; }
        }

        // barra propia (la de Windows es azul y no se puede pintar): pista roja y un rombo para arrastrar
        class DSlider : Control
        {
            public int Minimum, Maximum = 100;
            int val; bool drag, hover;
            public event EventHandler ValueChanged, Released;
            const int Pad = 10;
            public DSlider()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand; TabStop = true;
            }
            public int Value
            {
                get { return val; }
                set { int v = Math.Max(Minimum, Math.Min(Maximum, value)); if (v == val) return; val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
            }
            int XOf(int v) { return Pad + (int)((long)(Width - 2 * Pad) * (v - Minimum) / Math.Max(1, Maximum - Minimum)); }
            void SetFromX(int x) { Value = Minimum + (int)Math.Round((double)(x - Pad) * (Maximum - Minimum) / Math.Max(1, Width - 2 * Pad)); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button != MouseButtons.Left) return; drag = true; Focus(); SetFromX(e.X); Invalidate(); }
            protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); }
            protected override void OnMouseUp(MouseEventArgs e) { if (!drag) return; drag = false; Invalidate(); if (Released != null) Released(this, EventArgs.Empty); }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
            protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                int big = Math.Max(1, (Maximum - Minimum) / 10);
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--;
                else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++;
                else if (e.KeyCode == Keys.PageDown) Value -= big;
                else if (e.KeyCode == Keys.PageUp) Value += big;
            }
            protected override void OnKeyUp(KeyEventArgs e) { if (Released != null) Released(this, EventArgs.Empty); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int cy = Height / 2, xv = XOf(val);
                using (var br = new SolidBrush(Dbd.Track)) g.FillRectangle(br, Pad, cy - 2, Width - 2 * Pad, 4);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(Pad, 0, Math.Max(1, xv - Pad + 1), 4), Color.FromArgb(90, 10, 16), Dbd.Acc, 0f))
                    g.FillRectangle(br, Pad, cy - 2, Math.Max(0, xv - Pad), 4);
                bool hot = hover || drag || Focused;
                using (var br = new SolidBrush(hot ? Dbd.AccHi : Dbd.Acc)) g.FillPolygon(br, Dbd.Diamond(xv, cy, 8));
                using (var pen = new Pen(Color.FromArgb(10, 6, 8), 1.5f)) g.DrawPolygon(pen, Dbd.Diamond(xv, cy, 8));
                using (var br = new SolidBrush(Color.FromArgb(hot ? 120 : 70, 255, 230, 225))) g.FillPolygon(br, Dbd.Diamond(xv, cy, 2.5f));
            }
        }

        // casilla con forma de rombo
        class DCheck : Control
        {
            bool chk, hover;
            public event EventHandler CheckedChanged;
            public DCheck()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand; TabStop = true; Height = 26;
            }
            public bool Checked
            {
                get { return chk; }
                set { if (chk == value) return; chk = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
            }
            protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } }
            protected override bool IsInputKey(Keys k) { return k == Keys.Space || base.IsInputKey(k); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) Checked = !Checked; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); }
            protected override void OnTextChanged(EventArgs e) { Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float cx = 10, cy = Height / 2f;
                bool on = Enabled;
                if (chk)
                {
                    using (var br = new SolidBrush(on ? (hover ? Dbd.AccHi : Dbd.Acc) : Dbd.Off)) g.FillPolygon(br, Dbd.Diamond(cx, cy, 8));
                    using (var br = new SolidBrush(Color.FromArgb(on ? 200 : 90, 255, 236, 230))) g.FillPolygon(br, Dbd.Diamond(cx, cy, 3));
                }
                else using (var pen = new Pen(on ? (hover ? Dbd.AccHi : Color.FromArgb(120, 100, 100)) : Dbd.Off, 1.6f)) g.DrawPolygon(pen, Dbd.Diamond(cx, cy, 7.5f));
                TextRenderer.DrawText(g, Text, Font, new Point(26, (Height - Font.Height) / 2), on ? Dbd.Fg : Dbd.Off, TextFormatFlags.NoPrefix);
            }
        }

        // item del costado: el elegido con una franja roja que se desvanece (como los menus del juego)
        class DNav : Control
        {
            public bool Selected; bool hover;
            public DNav() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                if (Selected || hover)
                {
                    var r = new Rectangle(0, 0, Width, Height);
                    using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(Selected ? 150 : 60, Dbd.Acc), Color.FromArgb(0, Dbd.Acc), 0f)) g.FillRectangle(br, r);
                    if (Selected) using (var br = new SolidBrush(Dbd.AccHi)) g.FillRectangle(br, 0, 0, 3, Height);
                }
                TextRenderer.DrawText(g, Text.ToUpperInvariant(), Font, new Rectangle(22, 0, Width - 22, Height), Selected ? Color.White : hover ? Dbd.Fg : Dbd.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        class SettingsWindow : Form
        {
            readonly Overlay ov;
            readonly Panel nav, body;
            readonly List<Action> refreshers = new List<Action>();
            readonly List<DNav> navItems = new List<DNav>();
            bool refreshing;
            int y;
            string cur;
            static readonly string[] Pages = { "Lista", "Mapa", "Rueda", "Teclas", "Sala", "Voz", "General" };
            static string PageLabel(string p)
            {
                if (!Lang.En) return p;
                switch (p) { case "Lista": return "List"; case "Mapa": return "Map"; case "Teclas": return "Keys"; case "Sala": return "Room"; case "Voz": return "Voice"; case "Rueda": return "Wheel"; default: return p; }
            }
            const int X = 24; int W = 420; // W: ancho del contenido (crece con la ventana)
            static Color Bg { get { return Dbd.Bg; } }
            static Color Fg { get { return Dbd.Fg; } }
            static Color Dim { get { return Dbd.Dim; } }

            [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

            public SettingsWindow(Overlay o, string page)
            {
                ov = o;
                Text = AppName + L(" · Configuración", " · Settings");
                FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(640, 580);
                MinimumSize = Size; // mas chica no: el contenido no entra
                // el tamano que le dejaste la ultima vez
                var sz = Regex.Match(ov.cfg.Get("VentanaConfig", ""), @"^(\d+)x(\d+)$");
                if (sz.Success)
                {
                    var wa = Screen.PrimaryScreen.WorkingArea;
                    Size = new Size(Math.Min(wa.Width, Math.Max(Width, int.Parse(sz.Groups[1].Value))), Math.Min(wa.Height, Math.Max(Height, int.Parse(sz.Groups[2].Value))));
                }
                // al terminar de agrandar/achicar: se reacomoda y se guarda el tamano
                int lastW = -1; bool sizing = false;
                ResizeBegin += (s, e) => sizing = true;
                ResizeEnd += (s, e) =>
                {
                    sizing = false;
                    if (WindowState == FormWindowState.Normal) ov.SetCfg("VentanaConfig", Width + "x" + Height);
                    if (body.ClientSize.Width != lastW && cur != null) { lastW = body.ClientSize.Width; ShowPage(cur); }
                };
                // maximizar / restaurar (sin arrastrar el borde)
                SizeChanged += (s, e) => { if (!sizing && WindowState != FormWindowState.Minimized && cur != null && body.ClientSize.Width != lastW) { lastW = body.ClientSize.Width; ShowPage(cur); } };
                BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9.5f);
                if (ov.trayRed != null) Icon = ov.trayRed;
                body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg };
                nav = new Panel { Dock = DockStyle.Left, Width = 160, BackColor = Dbd.Side };
                Controls.Add(body); Controls.Add(nav);
                // costado: nombre arriba, un resplandor rojo abajo y la version
                nav.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    var glow = new Rectangle(0, nav.Height - 220, nav.Width, 220);
                    using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(glow.X, glow.Y - 2, glow.Width, glow.Height + 4), Color.FromArgb(0, 80, 6, 12), Color.FromArgb(110, 80, 6, 12), 90f)) g.FillRectangle(br, glow);
                    TextRenderer.DrawText(g, "CALLOUTS", Dbd.Head(21f), new Point(18, 16), Dbd.AccHi, TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "IN THE FOG", Dbd.Head(11f), new Point(21, 50), Dbd.Fg, TextFormatFlags.NoPrefix);
                    using (var br = new SolidBrush(Dbd.Acc)) g.FillRectangle(br, 21, 74, 34, 2);
                    TextRenderer.DrawText(g, "v" + Updater.AppVersion, Dbd.Cond(9f), new Point(21, nav.Height - 28), Dbd.Dim, TextFormatFlags.NoPrefix);
                    using (var pen = new Pen(Dbd.Line)) g.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
                };
                int ny = 96;
                foreach (var p in Pages)
                {
                    var l = new DNav { Text = PageLabel(p), Tag = p, Bounds = new Rectangle(0, ny, 159, 40), Font = Dbd.Head(11.5f), BackColor = Dbd.Side };
                    string pp = p;
                    l.Click += (s, e) => ShowPage(pp);
                    nav.Controls.Add(l); navItems.Add(l);
                    ny += 42;
                }
                // al volver a la ventana (por ejemplo despues de elegir una tecla o unirse a una sala) se actualiza lo que muestra
                Activated += (s, e) => Refresh2();
                ShowPage(page ?? "Lista");
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // barra de titulo oscura (Windows 10 1809+ / 11) y barra de scroll oscura
                try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
                try { SetWindowTheme(body.Handle, "DarkMode_Explorer", null); } catch { }
            }

            internal void Refresh2() { refreshing = true; try { foreach (var r in refreshers) r(); } finally { refreshing = false; } }

            public void ShowPage(string page)
            {
                if (Array.IndexOf(Pages, page) < 0) page = "Lista";
                cur = page;
                W = Math.Max(420, Math.Min(900, body.ClientSize.Width - X * 2 - SystemInformation.VerticalScrollBarWidth - 4));
                foreach (var l in navItems) { l.Selected = (string)l.Tag == page; l.Invalidate(); }
                body.SuspendLayout();
                body.AutoScrollPosition = Point.Empty;
                var old = body.Controls.Cast<Control>().ToList();
                body.Controls.Clear();
                foreach (var c in old) c.Dispose();
                refreshers.Clear();
                y = 22;
                switch (page)
                {
                    case "Lista": PageList(); break;
                    case "Mapa": PageMap(); break;
                    case "Teclas": PageKeys(); break;
                    case "Rueda": PageWheel(); break;
                    case "Sala": PageRoom(); break;
                    case "Voz": PageVoice(); break;
                    default: PageGeneral(); break;
                }
                body.Controls.Add(new Label { Bounds = new Rectangle(X, y, 10, 14) }); // margen de abajo al scrollear
                body.ResumeLayout();
                Refresh2();
            }

            // ---- piezas
            void Title(string t)
            {
                if (y > 22) y += 14;
                body.Controls.Add(new Label { Text = t.ToUpperInvariant(), AutoSize = true, Location = new Point(X - 1, y), Font = Dbd.Head(15f), ForeColor = Fg });
                y += 32;
                // linea roja que se desvanece
                var ln = new Panel { Bounds = new Rectangle(X, y, W, 2), BackColor = Bg };
                ln.Paint += (s, e) => { using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(ln.ClientRectangle, Dbd.Acc, Color.FromArgb(0, Dbd.Acc), 0f)) e.Graphics.FillRectangle(br, ln.ClientRectangle); };
                body.Controls.Add(ln);
                y += 14;
            }
            void Note(string t)
            {
                var l = new Label { Text = t, AutoSize = false, Location = new Point(X, y), ForeColor = Dim, Font = new Font("Segoe UI", 8.75f) };
                l.Size = new Size(W, TextRenderer.MeasureText(t, l.Font, new Size(W, 1000), TextFormatFlags.WordBreak).Height + 2);
                body.Controls.Add(l);
                y += l.Height + 8;
            }
            DCheck Check(string t, Func<bool> get, Action<bool> set)
            {
                var c = new DCheck { Text = t, Location = new Point(X - 1, y), BackColor = Bg, ForeColor = Fg, Font = Font };
                c.Width = 30 + TextRenderer.MeasureText(t, Font).Width;
                c.CheckedChanged += (s, e) => { if (!refreshing) set(c.Checked); };
                refreshers.Add(() => c.Checked = get());
                body.Controls.Add(c);
                y += 32;
                return c;
            }
            // barra: cambia en vivo; se guarda en el archivo al soltarla
            void Slider(string t, int min, int max, Func<int> get, Action<int> live, Func<int, string> fmt)
            {
                var l = new Label { AutoSize = true, Location = new Point(X, y), ForeColor = Fg, Text = t };
                var v = new Label { AutoSize = false, Bounds = new Rectangle(X + W - 100, y, 100, 20), ForeColor = Dbd.AccHi, TextAlign = ContentAlignment.TopRight, Font = Dbd.Head(10.5f) };
                var tb = new DSlider { Minimum = min, Maximum = max, Bounds = new Rectangle(X - 10, y + 20, W + 20, 26), BackColor = Bg };
                tb.ValueChanged += (s, e) => { v.Text = fmt(tb.Value); if (!refreshing) live(tb.Value); };
                tb.Released += (s, e) => ov.FlushCfg();
                refreshers.Add(() => { tb.Value = Math.Max(min, Math.Min(max, get())); v.Text = fmt(tb.Value); });
                body.Controls.Add(l); body.Controls.Add(v); body.Controls.Add(tb);
                y += 54;
            }
            void Choice(string t, string[] names, Func<int> get, Action<int> set)
            {
                body.Controls.Add(new Label { Text = t, AutoSize = true, Location = new Point(X, y + 4), ForeColor = Fg });
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Dbd.Ctl, ForeColor = Fg, Bounds = new Rectangle(X + 180, y, W - 180, 26), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
                // dibujada a mano: la de Windows queda blanca sobre el fondo oscuro
                cb.DrawItem += (s, e) =>
                {
                    bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                    using (var br = new SolidBrush(sel ? Dbd.Acc : Dbd.Ctl)) e.Graphics.FillRectangle(br, e.Bounds);
                    if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, (string)cb.Items[e.Index], cb.Font, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), Fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
                cb.HandleCreated += (s, e) => { try { SetWindowTheme(cb.Handle, "DarkMode_CFD", null); } catch { } };
                cb.Items.AddRange(names);
                cb.SelectedIndexChanged += (s, e) => { if (!refreshing && cb.SelectedIndex >= 0) set(cb.SelectedIndex); };
                refreshers.Add(() => { int i = get(); cb.SelectedIndex = i >= 0 && i < names.Length ? i : -1; });
                body.Controls.Add(cb);
                y += 38;
            }
            Button Btn(string t, int x, Action a)
            {
                var b = new Button { Text = t.ToUpperInvariant(), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(x, y), FlatStyle = FlatStyle.Flat, BackColor = Dbd.Ctl, ForeColor = Fg, Padding = new Padding(10, 3, 10, 3), Cursor = Cursors.Hand, Font = Dbd.Head(10f), UseMnemonic = false };
                b.FlatAppearance.BorderColor = Color.FromArgb(96, 20, 28);
                b.FlatAppearance.MouseOverBackColor = Dbd.CtlHi;
                b.FlatAppearance.MouseDownBackColor = Dbd.Acc;
                b.Click += (s, e) => { a(); Refresh2(); };
                b.EnabledChanged += (s, e) => b.FlatAppearance.BorderColor = b.Enabled ? Color.FromArgb(96, 20, 28) : Dbd.Line;
                body.Controls.Add(b);
                return b;
            }
            // varios botones en una fila (el texto puede cambiar: se reacomodan solos)
            Button[] Buttons(params Tuple<string, Action, Func<bool>>[] bs)
            {
                var list = new List<Button>();
                foreach (var t in bs)
                {
                    var b = Btn(t.Item1, X, t.Item2);
                    var en = t.Item3;
                    if (en != null) refreshers.Add(() => b.Enabled = en());
                    list.Add(b);
                }
                var arr = list.ToArray();
                EventHandler lay = (s, e) => { int x = X; foreach (var b in arr) { b.Left = x; x += b.Width + 8; } };
                foreach (var b in arr) b.SizeChanged += lay;
                lay(null, null);
                y += 42;
                return arr;
            }
            static Tuple<string, Action, Func<bool>> B(string t, Action a) { return Tuple.Create(t, a, (Func<bool>)null); }
            static Tuple<string, Action, Func<bool>> B(string t, Action a, Func<bool> en) { return Tuple.Create(t, a, en); }
            Label Value(Func<string> get)
            {
                var l = new Label { AutoSize = false, Location = new Point(X, y), Size = new Size(W, 24), ForeColor = Fg, Font = Dbd.Head(11f), UseMnemonic = false };
                refreshers.Add(() => l.Text = get());
                body.Controls.Add(l);
                y += 28;
                return l;
            }
            Cfg C { get { return ov.cfg; } }
            void Look() { ov.MakeFonts(); ov.Relayout(); }

            // ---- paginas
            void PageList()
            {
                var scr = Screen.PrimaryScreen.Bounds;
                Title(L("Lista de perks", "Perk list"));
                var sample = Buttons(B(L("Ver una lista de ejemplo", "Show a sample list"), () => { if (ov.SampleOn) ov.EndSample(); else ov.StartSample(); }, () => ov.SampleOn || !ov.HasAnything()))[0];
                refreshers.Add(() => sample.Text = (ov.SampleOn ? L("Quitar la lista de ejemplo", "Remove the sample list") : L("Ver una lista de ejemplo", "Show a sample list")).ToUpperInvariant());
                Note(L("Con la lista vacía podés poner una de ejemplo para ver cómo queda cada cambio. Se quita sola al cerrar esta ventana.", "With an empty list you can show a sample one to see how each change looks. It goes away when you close this window."));
                Choice(L("Esquina de la pantalla", "Screen corner"), Corners.Select(c => c.Item2).ToArray(),
                    () => { string p = C.Get("Posicion", "arriba-derecha").ToLowerInvariant(); return (p.Contains("abajo") ? 2 : 0) + (p.Contains("izq") ? 1 : 0); },
                    i => { ov.SetCfg("Posicion", Corners[i].Item1); if (ov.tip != null) ov.tip.Hide(); ov.Relayout(); });
                Slider(L("Distancia al borde (de costado)", "Distance from the side edge"), 0, scr.Width / 2, () => C.GetInt("MargenX", 30), v => { ov.Live("MargenX", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider(L("Distancia al borde (de arriba o abajo)", "Distance from the top/bottom edge"), 0, scr.Height / 2, () => C.GetInt("MargenY", 140), v => { ov.Live("MargenY", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider(L("Escala general", "Overall scale"), 50, 200, () => C.GetInt("Escala", 100), v => { ov.Live("Escala", v.ToString()); Look(); }, v => v + " %");
                Slider(L("Tamaño de los íconos", "Icon size"), 24, 120, () => C.GetInt("TamanoPerk", 72), v => { ov.Live("TamanoPerk", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider(L("Tamaño de la letra", "Text size"), 8, 28, () => C.GetInt("TamanoLetra", 14), v => { ov.Live("TamanoLetra", v.ToString()); Look(); }, v => v.ToString());
                Slider(L("Opacidad", "Opacity"), 30, 100, () => (int)Math.Round(C.GetD("Opacidad", 0.85) * 100),
                    v => { ov.Live("Opacidad", (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture)); ov.Opacity = SafeOp(v / 100.0); }, v => v + " %");
                Check(L("Mostrar los nombres de perks y addons", "Show perk and add-on names"), () => ov.showNames, v => ov.SetNames(v));
                Check(L("Mostrar el nombre del killer", "Show the killer's name"), () => ov.KillerOn, v => { ov.SetCfg("MostrarKiller", v ? "si" : "no"); ov.Relayout(); });
                Slider(L("Tamaño del nombre del killer", "Killer name size"), 6, 22, () => C.GetInt("TamanoKiller", 12), v => { ov.Live("TamanoKiller", v.ToString()); ov.killerFont = null; ov.Relayout(); }, v => v.ToString());
                Check(L("Mostrar solo con el menú de DBD abierto (ESC)", "Show only while the DBD menu is open (ESC)"), () => ov.MenuOnly, v => ov.SetCfg("Visibilidad", v ? "menu" : "siempre"));
                Check(L("Clics en la lista: doble clic confirma una sospecha (?), mantener apretado quita", "Clicks on the list: double-click confirms a guess (?), hold removes"),
                      () => C.Get("ListaClics", "si") != "no", v => ov.SetCfg("ListaClics", v ? "si" : "no"));
                Check(L("Builds sugeridas (hens333.com): al anotar el killer, los lugares libres muestran una build; su tecla pasa a la siguiente (o la quita), doble clic anota y mantener apretado saca",
                        "Suggested builds (hens333.com): once the killer is noted, empty slots show a build; its key cycles builds (or hides it), double-click notes a perk and holding removes it"),
                      () => C.Get("BuildsSugeridas", "no") == "si", v => { ov.SetCfg("BuildsSugeridas", v ? "si" : "no"); ov.Relayout(); });
                Check(L("Mostrar el botón de builds (abajo a la derecha de los perks)", "Show the builds button (bottom right of the perks)"),
                      () => C.Get("BotonBuild", "si") != "no", v => { ov.SetCfg("BotonBuild", v ? "si" : "no"); ov.Invalidate(); });
                Slider(L("Tamaño del botón de builds", "Builds button size"), 30, 100, () => C.GetInt("TamanoBotonBuild", 60), v => { ov.Live("TamanoBotonBuild", v.ToString()); ov.Invalidate(); }, v => v + " %");
                Note(L("Mientras jugás la lista se oculta y aparece al abrir el menú ESC, al escribir con ", "While you play the list is hidden; it shows up when you open the ESC menu, when you type with ") + ov.TypeKeyText + L(" o cuando hay un mensaje.", " or when there's a message."));
            }

            void PageMap()
            {
                var scr = Screen.PrimaryScreen.Bounds;
                Title(L("Mapa", "Map"));
                var show = Buttons(B(L("Ver un mapa de ejemplo", "Show a sample map"), () =>
                {
                    if (ov.mapShown != null) { ov.HideMap(false); return; }
                    var m = ov.cat.Maps.FirstOrDefault(x => ov.cat.MapFiles.ContainsKey(x.Name));
                    if (m != null) ov.ShowMap(m.Name, false);
                }, () => ov.cat.MapFiles.Count > 0))[0];
                refreshers.Add(() => show.Text = (ov.mapShown != null ? L("Ocultar el mapa", "Hide the map") : L("Ver un mapa de ejemplo", "Show a sample map")).ToUpperInvariant());
                Note(Lang.En ? "Pick a map by typing its name in the " + ov.TypeKeyText + " box (for example \"map coal\"). In a room, everyone sees it." : "El mapa se elige escribiendo su nombre en el cuadro de " + ov.TypeKeyText + " (por ejemplo \"mapa coal\"). Cuando estás en sala, lo ven todos.");
                Slider(L("Tamaño", "Size"), 160, 700, () => ov.MapSize, v => { ov.Live("TamanoMapa", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Slider(L("Opacidad", "Opacity"), 15, 100, () => ov.MapOpacity, v => { ov.Live("OpacidadMapa", v.ToString()); if (ov.mapWin != null) ov.mapWin.Opacity = SafeOp(v / 100.0); }, v => v + " %");
                Slider(L("Posición desde la izquierda", "Position from the left"), 0, Math.Max(100, scr.Width - 160), () => C.GetInt("MapaX", 20), v => { ov.Live("MapaX", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Slider(L("Posición desde arriba", "Position from the top"), 0, Math.Max(100, scr.Height - 160), () => C.GetInt("MapaY", 20), v => { ov.Live("MapaY", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Check(L("Mostrar solo con el menú de DBD abierto (ESC)", "Show only while the DBD menu is open (ESC)"), () => ov.MapMenuOnly, v => { ov.SetCfg("MapaVisibilidad", v ? "menu" : "siempre"); ov.UpdateMapVisibility(ov.cursorShown); });
                Note(L("Planos con callouts de Lethia (hens333.com/callouts), versión de Zexov sobre el original de Broosley y Evo.", "Callout maps by Lethia (hens333.com/callouts), Zexov's version of the original by Broosley and Evo."));
            }

            void KeyRow(Tuple<string, string, string, bool> a)
            {
                body.Controls.Add(new Label { Text = a.Item1, AutoSize = true, Location = new Point(X, y + 6), ForeColor = Fg });
                var v = new Label { AutoSize = false, Bounds = new Rectangle(X + 215, y + 4, 115, 22), ForeColor = Dbd.AccHi, Font = Dbd.Head(11f), UseMnemonic = false };
                refreshers.Add(() => { string k = C.Get(a.Item2, a.Item3); v.Text = k.Length > 0 ? k : L("sin tecla", "no key"); });
                body.Controls.Add(v);
                Btn(L("Cambiar…", "Change…"), X + 330, () => ov.AskKey(a));
                y += 40;
            }

            void PadRow(string label, string key, string def)
            {
                body.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(X, y + 6), ForeColor = Fg });
                var v = new Label { AutoSize = false, Bounds = new Rectangle(X + 215, y + 4, 115, 22), ForeColor = Dbd.AccHi, Font = Dbd.Head(11f), UseMnemonic = false };
                refreshers.Add(() => v.Text = ov.PadText(ov.PadMask(key, def)));
                body.Controls.Add(v);
                Btn(L("Cambiar…", "Change…"), X + 330, () => ov.PadRecord(key));
                y += 40;
            }
            void PageWheel()
            {
                Title(L("Rueda", "Wheel"));
                Note(L("Otra forma de anotar, con el mouse: con el menú de DBD abierto (ESC), apretá la tecla y elegí. Clic derecho o el centro: atrás. ESC: cerrar.",
                       "Another way to add things, with the mouse: with the DBD menu open (ESC), press the key and pick. Right-click or the center: back. ESC: close."));
                KeyRow(KeyActions.First(k => k.Item2 == "TeclaRueda"));
                var wmodes = new[] { "clic", "gesto", "gestoclic" };
                Choice(L("Cómo se usa", "How it works"), new[] { L("Con el cursor (menú ESC)", "With the cursor (ESC menu)"), L("Mantener y empujar", "Hold and push"), L("Mantener y hacer clic", "Hold and click") },
                    () => Math.Max(0, Array.IndexOf(wmodes, C.Get("RuedaModo", "clic"))), i => ov.SetCfg("RuedaModo", wmodes[i]));
                Note(L("Mantener y empujar: con la tecla apretada, empujá el mouse hacia una categoría hasta el borde para entrar; en la última, soltá para elegir. Clic derecho: atrás.\nMantener y hacer clic: apuntá y hacé clic; soltar la tecla cierra.\nAndan también jugando, pero la cámara se mueve y el juego recibe los clics (la app no los bloquea).",
                       "Hold and push: with the key held, push the mouse toward a category to the edge to enter; on the last wheel, release to pick. Right-click: back.\nHold and click: aim and click; releasing the key closes.\nBoth work while playing, but the camera moves and the game gets the clicks (the app doesn't block them)."));
                Check(L("Mostrar el puntero al apuntar (modos con la tecla)", "Show the pointer while aiming (key modes)"),
                      () => C.Get("RuedaPuntero", "no") == "si", v => ov.SetCfg("RuedaPuntero", v ? "si" : "no"));
                Buttons(B(L("Probar la rueda", "Try the wheel"), () => ov.ToggleWheel()));
                Title(L("Gamepad", "Gamepad"));
                Check(L("Usar la rueda con gamepad (PlayStation, Xbox y otros)", "Use the wheel with a gamepad (PlayStation, Xbox and others)"),
                      () => ov.PadOn, v => { ov.SetCfg("Gamepad", v ? "si" : "no"); ov.UpdatePad(); });
                PadRow(L("Abrir (mantener)", "Open (hold)"), "PadAbrir", "L2");
                PadRow(L("Elegir", "Pick"), "PadElegir", "R2");
                Note(L("Mantené el botón de abrir y apuntá con el stick derecho; el de elegir elige y, con el stick al centro, vuelve atrás. Soltar cierra. El juego también recibe los botones (la app no los bloquea): si chocan con algo, cambialos.",
                       "Hold the open button and aim with the right stick; the pick button picks and, with the stick centered, goes back. Releasing closes. The game also gets the buttons (the app doesn't block them): if they clash, change them."));
                var pmodes = new[] { "categorias", "letras" };
                Choice(L("Perks", "Perks"), new[] { L("Por lo que hacen (más usados primero)", "By what they do (most used first)"), L("Por letra", "By letter") },
                    () => Math.Max(0, Array.IndexOf(pmodes, C.Get("RuedaPerks", "categorias"))), i => ov.SetCfg("RuedaPerks", pmodes[i]));
                Note(L("Por letra sigue el idioma de los nombres (Configuración → General).", "By letter follows the names language (Settings → General)."));
                var mmodes = new[] { "reino", "letras" };
                Choice(L("Mapas", "Maps"), new[] { L("Por reino", "By realm"), L("Por letra", "By letter") },
                    () => Math.Max(0, Array.IndexOf(mmodes, C.Get("RuedaMapas", "reino"))), i => ov.SetCfg("RuedaMapas", mmodes[i]));
                var kmodes = new[] { "poder", "licencia", "jugados", "letras" };
                Choice(L("Killers", "Killers"), new[] { L("Por tipo de poder", "By power type"), L("Originales y licenciados", "Originals and licensed"),
                                                        L("Más jugados primero", "Most played first"), L("Por letra", "By letter") },
                    () => Math.Max(0, Array.IndexOf(kmodes, C.Get("RuedaKillers", "poder"))), i => ov.SetCfg("RuedaKillers", kmodes[i]));
                Check(L("Color por tier en los perks (S, A, B, C, D), en la rueda y al escribir", "Perk tier colors (S, A, B, C, D), in the wheel and while typing"),
                      () => C.Get("RuedaTiers", "si") != "no", v => ov.SetCfg("RuedaTiers", v ? "si" : "no"));
                Title(L("Descripciones", "Descriptions"));
                Check(L("Mostrar qué hace cada ofrenda y addon al pasar el mouse", "Show what each offering and add-on does on hover"),
                      () => C.Get("RuedaDescripcion", "si") != "no", v => ov.SetCfg("RuedaDescripcion", v ? "si" : "no"));
                var stModes = new[] { "ambos", "icono", "texto" };
                Choice(L("Estados", "Status effects"),
                    new[] { L("Ícono y nombre", "Icon and name"), L("Solo ícono", "Icon only"), L("Solo nombre", "Name only") },
                    () => Math.Max(0, Array.IndexOf(stModes, C.Get("EstadosRueda", "ambos"))), i => ov.SetCfg("EstadosRueda", stModes[i]));
                Note(L("Agotamiento, Ceguera, Vulnerabilidad…: con el ícono del juego, el nombre o los dos.", "Exhausted, Blindness, Exposed…: with the game icon, the name or both."));
            }

            void PageKeys()
            {
                Title(L("Teclas", "Keys"));
                foreach (var act in KeyActions) KeyRow(act);
                y += 4;
                Check(L("Las teclas solo funcionan con DBD al frente", "Keys only work while DBD is in front"), () => ov.KeysOnlyInDbd, v => ov.SetCfg("TeclasSoloEnDBD", v ? "si" : "no"));
                Note(L("Así, escribiendo en Discord o en el navegador, las teclas de la app no hacen nada. Al cambiar una tecla podés usar combinaciones (Shift, Ctrl, Alt) o un botón del mouse.", "So typing in Discord or the browser doesn't trigger the app's keys. When changing a key you can use combinations (Shift, Ctrl, Alt) or a mouse button."));
            }

            void PageRoom()
            {
                Title(L("Sala compartida", "Shared room"));
                Value(() => ov.RoomText());
                Note(L("En sala, lo que anota uno lo ven todos.", "In a room, what one person adds, everyone sees."));
                Check(L("Automática con mi grupo de Steam", "Automatic with my Steam party"), () => ov.AutoRoom, v => ov.SetAuto(v));
                Note(L("Con DBD abierto, todos los del mismo grupo de Steam que tengan la app quedan juntos, sin hacer nada.", "With DBD open, everyone in the same Steam party who has the app is grouped automatically."));
                Title(L("Sala con código", "Room with a code"));
                Note(Lang.En ? "To play with people who are not in your Steam party, like solo queue teammates or friends on Epic (up to 4 per room). You can also use the " + ov.TypeKeyText + " box: \"room new\", \"room CODE\" or \"room leave\"."
                             : "Para jugar con gente que no está en tu grupo de Steam, como randoms o jugadores de Epic (hasta 4 por sala). También se maneja desde el cuadro de " + ov.TypeKeyText + " escribiendo \"sala nueva\", \"sala CÓDIGO\" o \"sala salir\".");
                Value(() => ov.ManualRoom ? L("Código: ", "Code: ") + ov.cfg.Get("Sala", "") : L("No estás en una sala con código", "You're not in a code room"));
                Buttons(B(L("Crear sala", "Create room"), () => ov.CreateRoom()), B(L("Unirse…", "Join…"), () => ov.JoinRoom()),
                        B(L("Copiar código", "Copy code"), () => { try { Clipboard.SetText(ov.cfg.Get("Sala", "")); } catch { } }, () => ov.ManualRoom),
                        B(L("Salir", "Leave"), () => ov.LeaveRoom(), () => ov.ManualRoom));
                Value(() => L("Tu nombre: ", "Your name: ") + (ov.MyName.Length > 0 ? ov.MyName : L("(sin nombre)", "(no name)")));
                Buttons(B(L("Cambiar mi nombre…", "Change my name…"), () => ov.AskName()));
                Check(L("Mostrar el ícono de sala en la lista", "Show the room icon on the list"), () => ov.ShowRoomLine, v => { ov.SetCfg("MostrarSala", v ? "si" : "no"); if (ov.roomLineItem != null) ov.roomLineItem.Checked = v; ov.Relayout(); });
            }

            void PageVoice()
            {
                Title(L("Voz", "Voice"));
                Note(Lang.En ? "Add things by talking, with Whisper (runs on your PC, no internet). It uses a fair amount of CPU while it understands a sentence: on low-end PCs keep it off and type with " + ov.TypeKeyText + "."
                             : "Anotar hablando con Whisper (corre en tu PC, sin internet). Usa bastante procesador mientras entiende una frase: en PCs con pocos recursos conviene dejarla apagada y anotar con " + ov.TypeKeyText + ".");
                Check(L("Reconocimiento de voz", "Voice recognition"), () => ov.VoiceOn, v => ov.SetVoice(v));
                var devs = new List<string> { L("Predeterminado de Windows", "Windows default") };
                try { devs.AddRange(Mic.Devices()); } catch { }
                Choice(L("Micrófono", "Microphone"), devs.ToArray(),
                    () => { string m = C.Get("Microfono", ""); int i = devs.FindIndex(d => d.Equals(m, StringComparison.OrdinalIgnoreCase)); return m.Length == 0 ? 0 : i; },
                    i => { ov.SetCfg("Microfono", i == 0 ? "" : devs[i]); if (ov.mic != null) ov.StartMic(); });
                var hands = Check(L("Hablar sin tecla (prueba)", "Talk without a key (beta)"), () => ov.HandsOn, v => ov.SetHands(v));
                var listen = Check(L("Escuchar Discord (prueba)", "Listen to Discord (beta)"), () => ov.ListenOn, v => ov.SetListen(v));
                var heard = Check(L("Mostrar lo que entiende Whisper", "Show what Whisper understands"), () => ov.ShowHeard, v => ov.SetCfg("VerTranscripcion", v ? "si" : "no"));
                Func<bool> inst = () => ov.voice || (ov.FindWhisperExe() != null && ov.FindModel() != null);
                refreshers.Add(() => { bool v = ov.VoiceOn; hands.Enabled = listen.Enabled = heard.Enabled = v; });
                var bs = Buttons(B(L("Ver todo lo que escuchó…", "See everything it heard…"), () =>
                        {
                            string path = Path.Combine(ov.dir, "logs", "escuchado.txt");
                            try { if (!File.Exists(path)) File.WriteAllText(path, "", Encoding.UTF8); Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
                        }), B(L("Instalar Whisper…", "Install Whisper…"), () => ov.InstallVoice(), () => !inst() && !ov.installing));
                refreshers.Add(() => bs[1].Text = (inst() ? L("Whisper instalado ✓", "Whisper installed ✓") : ov.installing ? L("Instalando Whisper…", "Installing Whisper…") : L("Instalar Whisper…", "Install Whisper…")).ToUpperInvariant());
            }

            void PageGeneral()
            {
                Title(L("Idioma", "Language"));
                Choice(L("Idioma de la app", "App language"), new[] { L("Automático (el de Windows)", "Automatic (Windows language)"), "Español", "English" },
                    () => Lang.Setting == "es" ? 1 : Lang.Setting == "en" ? 2 : 0,
                    i =>
                    {
                        string v = i == 1 ? "es" : i == 2 ? "en" : "auto";
                        if (v == Lang.Setting) return;
                        ov.SetCfg("IdiomaApp", v);
                        bool en = i == 2 || (i == 0 && !CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase));
                        if (en == Lang.En) { Lang.Setting = v; return; }
                        ov.RestartApp();
                    });
                Note(L("Al cambiarlo la app se reinicia (la lista y la sala se mantienen).", "Changing it restarts the app (the list and the room are kept)."));
                var langs = new[] { L("Inglés", "English"), L("Español", "Spanish") };
                Choice(L("Nombres de perks y addons", "Perk and add-on names"), langs, () => C.Get("IdiomaNombres", "en").ToLowerInvariant().StartsWith("es") ? 1 : 0,
                    i => { ov.SetCfg("IdiomaNombres", i == 1 ? "es" : "en"); if (ov.tip != null) { ov.tip.Hide(); ov.hoverE = null; } ov.Relayout(); });
                Choice(L("Descripciones", "Descriptions"), langs, () => C.Get("IdiomaDescripciones", Lang.En ? "en" : "es").ToLowerInvariant().StartsWith("es") ? 1 : 0,
                    i => { ov.SetCfg("IdiomaDescripciones", i == 1 ? "es" : "en"); if (ov.tip != null) { ov.tip.Hide(); ov.hoverE = null; } ov.Relayout(); });
                Title("App");
                Value(() => L("Versión ", "Version ") + Updater.AppVersion);
                Buttons(B(L("Buscar actualización", "Check for updates"), () => { if (ov.updating) return; ov.Feedback(L("Buscando actualización…", "Checking for updates…"), Color.FromArgb(200, 200, 210), 4000); ov.CheckUpdate(true); }),
                        B(L("Abrir carpeta", "Open folder"), () => { try { Process.Start("explorer.exe", "\"" + ov.dir + "\""); } catch { } }));
                Buttons(B(L("Editar apodos", "Edit nicknames"), () => { try { ov.st.ReloadAliases(); Process.Start("notepad.exe", "\"" + Path.Combine(ov.dir, "apodos.txt") + "\""); } catch { } }),
                        B(L("Editar config.ini (avanzado)", "Edit config.ini (advanced)"), () => { ov.FlushCfg(); try { Process.Start("notepad.exe", "\"" + Path.Combine(ov.dir, "config.ini") + "\""); } catch { } }));
                Note(L("Los apodos son nombres alternativos para perks, addons o killers (por ejemplo \"ruina\" para Hex: Ruin).", "Nicknames are alternative names for perks, add-ons or killers (for example \"ruin\" for Hex: Ruin)."));
                Buttons(B(L("Ver la guía de la primera vez", "Show the first-time guide"), () => ov.ShowWelcome()));
                Title(L("Apariencia", "Appearance"));
                Buttons(B(L("Volver la apariencia a la de fábrica", "Reset the appearance to defaults"), () =>
                {
                    if (MessageBox.Show(this, L("¿Volver el tamaño, la posición y la opacidad de la lista y del mapa a los valores de fábrica?\n(Las teclas, la sala y la voz no se tocan.)", "Reset the size, position and opacity of the list and the map to their defaults?\n(Keys, room and voice are not changed.)"), AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var d in LookDefaults) ov.SetCfg(d.Item1, d.Item2);
                    ov.Opacity = 0.85; ov.MakeFonts(); ov.Relayout(); ov.RefreshMap();
                    ov.Feedback(L("apariencia de fábrica", "default appearance"), Color.Silver, 3000);
                }));
            }
        }

        // ---- guia de la primera vez: 3 pasos (anotar, compartir, mapas/ayuda). Sale sola la primera vez y desde Configuracion -> General.
        WelcomeWindow welcomeWin;
        public void ShowWelcome()
        {
            if (welcomeWin != null && !welcomeWin.IsDisposed) { welcomeWin.Activate(); return; }
            welcomeWin = new WelcomeWindow(this);
            welcomeWin.FormClosed += (s, e) => { SetCfg("GuiaVista", "si"); welcomeWin = null; };
            welcomeWin.Show();
            welcomeWin.Activate();
        }

        class WelcomeWindow : Form
        {
            readonly Overlay ov;
            int step;
            readonly Label title, body;
            readonly PictureBox pic;
            readonly Button back, next, tryIt;
            readonly Image[] gifs = new Image[3];
            [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

            // titulo y una o dos lineas: lo demas lo muestra el GIF
            string[][] Steps()
            {
                string t = ov.TypeKeyText, h = ov.HelpKeyName;
                if (Lang.En) return new[] {
                    new[] { "Write down what you see",
                        t + ", a few letters, Enter. Don't know the name? Search by what it does: \"addon 15 slow\". Add \"?\" to mark a guess (\"noed?\")." },
                    new[] { "All four see it",
                        "Steam party: automatic. Solo queue: type \"room new\" and paste the code in the lobby chat; the others type \"room\" + the code." },
                    new[] { "Maps, help and settings",
                        "\"map coal\" shows the callout map · hold " + h + " for every command · double-click the hook icon in the taskbar for settings." } };
                return new[] {
                    new[] { "Anotá lo que ves",
                        t + ", unas letras, Enter. ¿No sabés el nombre? Buscalo por lo que hace: \"addon 15 lento\". Con \"?\" queda como sospecha (\"noed?\")." },
                    new[] { "Lo ven los cuatro",
                        "Grupo de Steam: automático. Con randoms: escribí \"sala nueva\" y pegá el código en el chat del lobby; los demás escriben \"sala\" + el código." },
                    new[] { "Mapas, ayuda y ajustes",
                        "\"mapa coal\" muestra el plano · mantené " + h + " para ver todos los comandos · doble clic en el ícono del gancho en la barra de tareas para la configuración." } };
            }

            static Image LoadGif(string name)
            {
                try
                {
                    var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                    return s != null ? Image.FromStream(s) : null; // el stream queda abierto: el GIF lo necesita para animarse
                }
                catch { return null; }
            }

            public WelcomeWindow(Overlay o)
            {
                ov = o;
                Text = AppName;
                FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(GW + 64, 580);
                BackColor = Dbd.Bg; ForeColor = Dbd.Fg; Font = new Font("Segoe UI", 10.5f);
                DoubleBuffered = true;
                if (ov.trayRed != null) Icon = ov.trayRed;
                string lang = Lang.En ? "en" : "es";
                for (int i = 0; i < 3; i++) gifs[i] = LoadGif("guia-" + lang + "-" + (i + 1) + ".gif");
                title = new Label { AutoSize = false, Bounds = new Rectangle(32, 112, GW, 32), Font = Dbd.Head(17f), ForeColor = Dbd.Fg, UseMnemonic = false };
                pic = new PictureBox { Bounds = new Rectangle(32, 150, GW, GH), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
                body = new Label { AutoSize = false, Bounds = new Rectangle(32, 150 + GH + 12, GW, 46), ForeColor = Color.FromArgb(200, 194, 188), UseMnemonic = false, BackColor = Color.Transparent };
                back = MakeBtn(L("Anterior", "Back"), () => Go(step - 1));
                next = MakeBtn("", () => { if (step < 2) Go(step + 1); else Close(); });
                tryIt = MakeBtn(L("Probar ahora", "Try it now"), () => ov.OpenTypeBox());
                Controls.AddRange(new Control[] { title, pic, body, back, next, tryIt });
                Go(0);
            }

            Button MakeBtn(string t, Action a)
            {
                var b = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, BackColor = Dbd.Ctl, ForeColor = Dbd.Fg,
                                     Padding = new Padding(12, 4, 12, 4), Cursor = Cursors.Hand, Font = Dbd.Head(10.5f), UseMnemonic = false, Text = t.ToUpperInvariant() };
                b.FlatAppearance.BorderColor = Color.FromArgb(96, 20, 28);
                b.FlatAppearance.MouseOverBackColor = Dbd.CtlHi;
                b.FlatAppearance.MouseDownBackColor = Dbd.Acc;
                b.Click += (s, e) => a();
                return b;
            }

            public void Go(int s)
            {
                step = Math.Max(0, Math.Min(2, s));
                var st = Steps()[step];
                title.Text = st[0].ToUpperInvariant();
                body.Text = st[1];
                pic.Image = gifs[step];
                pic.Visible = gifs[step] != null;
                // sin GIF (no deberia pasar): el texto ocupa su lugar
                body.Top = pic.Visible ? pic.Bottom + 12 : pic.Top;
                back.Visible = step > 0;
                tryIt.Visible = step == 0;
                next.Text = (step < 2 ? L("Siguiente", "Next") : L("Empezar", "Let's go")).ToUpperInvariant();
                next.BackColor = step < 2 ? Dbd.Ctl : Dbd.Acc;
                int by = ClientSize.Height - 54;
                next.Location = new Point(ClientSize.Width - 32 - next.PreferredSize.Width, by);
                back.Location = new Point(next.Left - 8 - back.PreferredSize.Width, by);
                tryIt.Location = new Point(32, by);
                Invalidate();
                next.Focus();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                pic.Image = null;
                foreach (var g in gifs) if (g != null) g.Dispose();
                base.OnFormClosed(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var glow = new Rectangle(0, ClientSize.Height - 140, ClientSize.Width, 140);
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(glow.X, glow.Y - 2, glow.Width, glow.Height + 4), Color.FromArgb(0, 80, 6, 12), Color.FromArgb(90, 80, 6, 12), 90f)) g.FillRectangle(br, glow);
                TextRenderer.DrawText(g, L("BIENVENIDO A", "WELCOME TO"), Dbd.Head(10f), new Point(32, 22), Dbd.Dim, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "CALLOUTS IN THE FOG", Dbd.Head(22f), new Point(30, 40), Dbd.AccHi, TextFormatFlags.NoPrefix);
                for (int i = 0; i < 3; i++)
                {
                    var d = Dbd.Diamond(ClientSize.Width - 92 + i * 26, 40, 7);
                    if (i == step) using (var b = new SolidBrush(Dbd.AccHi)) g.FillPolygon(b, d);
                    else if (i < step) using (var b = new SolidBrush(Dbd.Acc)) g.FillPolygon(b, d);
                    using (var p = new Pen(i <= step ? Dbd.AccHi : Dbd.Off, 1.5f)) g.DrawPolygon(p, d);
                }
                TextRenderer.DrawText(g, (step + 1) + " / 3", Dbd.Cond(9f), new Point(ClientSize.Width - 96, 56), Dbd.Dim, TextFormatFlags.NoPrefix);
                using (var p = new Pen(Dbd.Line)) g.DrawLine(p, 32, 96, ClientSize.Width - 32, 96);
                using (var p = new Pen(Dbd.Acc, 2f)) g.DrawLine(p, 32, 96, 32 + (ClientSize.Width - 64) * (step + 1) / 3, 96);
                using (var p = new Pen(Dbd.Line)) g.DrawRectangle(p, pic.Left - 1, pic.Top - 1, pic.Width + 1, pic.Height + 1);
            }
        }

        // ---- GIFs de la guia: `DbdOverlay.exe --gifs carpeta` (en una copia con su config.ini) dibuja cuadro por cuadro lo
        // que hace la app (cuadro de escribir, lista, mapa, ayuda) y deja PNGs + list.txt para armar el GIF con ffmpeg.
        // No corre nunca al jugar.
        public static string GifOut;
        const int GW = 700, GH = 300;
        List<Tuple<Bitmap, int>> gifFrames = new List<Tuple<Bitmap, int>>();
        TypeBox gifBox; bool gifBoxOn; string gifKey, gifMapFile;
        // pantalla partida (paso 2): lo que ve cada uno
        bool gifSplit; Bitmap gifL, gifR; string gifKeyL, gifTypedL, gifKeyR, gifTypedR;
        static Bitmap gifBg;

        Bitmap CapOverlay()
        {
            Relayout();
            int w = Math.Max(1, Width), h = Math.Max(1, Height);
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp)) OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, w, h)));
            bmp.MakeTransparent(KeyColor);
            return bmp;
        }

        static Bitmap GifBg()
        {
            if (gifBg != null) return gifBg;
            gifBg = new Bitmap(GW, GH);
            using (var g = Graphics.FromImage(gifBg))
            {
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, GW, GH), Color.FromArgb(38, 40, 46), Color.FromArgb(10, 10, 13), 90f))
                    g.FillRectangle(br, 0, 0, GW, GH);
                var rnd = new Random(7);
                for (int i = 0; i < 30; i++)
                {
                    int rw = rnd.Next(140, 340), rh = rnd.Next(40, 110), x = rnd.Next(-120, GW), y = rnd.Next(GH / 4, GH);
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddEllipse(x, y, rw, rh);
                        using (var pb = new System.Drawing.Drawing2D.PathGradientBrush(path) { CenterColor = Color.FromArgb(rnd.Next(12, 30), 200, 205, 215), SurroundColors = new[] { Color.FromArgb(0, 200, 205, 215) } })
                            g.FillPath(pb, path);
                    }
                }
            }
            return gifBg;
        }

        static void GifKeycap(Graphics g, string k, int x, int y)
        {
            var f = Dbd.Head(13f);
            var sz = TextRenderer.MeasureText(k, f);
            var r = new Rectangle(x, y, sz.Width + 18, sz.Height + 10);
            using (var b = new SolidBrush(Color.FromArgb(235, 26, 20, 22))) g.FillRectangle(b, r);
            using (var p = new Pen(Dbd.AccHi, 2f)) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, k, f, r, Dbd.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        // lo escrito, como en el cuadro (para la pantalla partida, donde el cuadro entero no entra)
        static void GifTyped(Graphics g, string t, int x, int y, int w)
        {
            var r = new Rectangle(x, y, w, 30);
            using (var b = new SolidBrush(Color.FromArgb(32, 32, 42))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(90, 90, 105))) g.DrawRectangle(p, r);
            TextRenderer.DrawText(g, t + "|", new Font("Segoe UI", 12f), new Point(x + 6, y + 4), Color.WhiteSmoke, TextFormatFlags.NoPrefix);
        }
        // la lista va arriba a la derecha (como en el juego); si no entra, se achica
        static void GifPlace(Graphics g, Bitmap b, int right, int top, int maxW, int maxH)
        {
            double s = Math.Min(1.0, Math.Min((double)maxW / b.Width, (double)maxH / b.Height));
            int w = (int)(b.Width * s), h = (int)(b.Height * s);
            g.DrawImage(b, right - w, top, w, h);
        }

        void Shot(int n)
        {
            var c = new Bitmap(GW, GH);
            using (var g = Graphics.FromImage(c))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(GifBg(), 0, 0);
                if (gifSplit)
                {
                    int half = GW / 2;
                    using (var p = new Pen(Color.FromArgb(120, 214, 32, 44), 2f)) g.DrawLine(p, half, 0, half, GH);
                    TextRenderer.DrawText(g, L("PANTALLA DE MEG", "MEG'S SCREEN"), Dbd.Head(11f), new Point(14, 10), Dbd.Dim, TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, L("PANTALLA DE DWIGHT", "DWIGHT'S SCREEN"), Dbd.Head(11f), new Point(half + 14, 10), Dbd.Dim, TextFormatFlags.NoPrefix);
                    if (gifL != null) GifPlace(g, gifL, half - 12, 38, half - 24, GH - 50);
                    if (gifR != null) GifPlace(g, gifR, GW - 12, 38, half - 24, GH - 50);
                    if (gifTypedL != null) GifTyped(g, gifTypedL, 14, GH - 92, 150);
                    if (gifTypedR != null) GifTyped(g, gifTypedR, half + 14, GH - 92, 150);
                    if (gifKeyL != null) GifKeycap(g, gifKeyL, 14, GH - 50);
                    if (gifKeyR != null) GifKeycap(g, gifKeyR, half + 14, GH - 50);
                }
                else
                {
                    using (var ovb = CapOverlay()) GifPlace(g, ovb, GW - 14, 14, helpShown ? GW - 28 : GW - 380, GH - 28);
                    if (gifMapFile != null)
                    {
                        var img = MapImage(gifMapFile);
                        if (img != null)
                        {
                            int mh = GH - 28, mw = Math.Min(330, mh * img.Width / Math.Max(1, img.Height));
                            mh = mw * img.Height / Math.Max(1, img.Width);
                            g.DrawImage(img, 16, 14, mw, mh);
                            using (var p = new Pen(Color.FromArgb(70, 70, 90))) g.DrawRectangle(p, 16, 14, mw, mh);
                        }
                    }
                    if (gifBoxOn)
                    {
                        using (var bb = new Bitmap(gifBox.Width, gifBox.Height))
                        {
                            gifBox.DrawToBitmap(bb, new Rectangle(0, 0, bb.Width, bb.Height));
                            double s = Math.Min(340.0 / bb.Width, (GH - 74.0) / bb.Height);
                            g.DrawImage(bb, 16, 14, (int)(bb.Width * s), (int)(bb.Height * s));
                        }
                    }
                    if (gifKey != null) GifKeycap(g, gifKey, gifMapFile != null ? 360 : 16, GH - 50);
                }
            }
            gifFrames.Add(Tuple.Create(c, n));
        }

        void GifSave(string name)
        {
            string d = Path.Combine(GifOut, name);
            Directory.CreateDirectory(d);
            var sb = new StringBuilder();
            for (int i = 0; i < gifFrames.Count; i++)
            {
                string f = i.ToString("000") + ".png";
                gifFrames[i].Item1.Save(Path.Combine(d, f));
                sb.Append("file '" + f + "'\nduration " + (gifFrames[i].Item2 * 0.1).ToString("0.0", CultureInfo.InvariantCulture) + "\n");
            }
            sb.Append("file '" + (gifFrames.Count - 1).ToString("000") + ".png'\n");
            File.WriteAllText(Path.Combine(d, "list.txt"), sb.ToString());
            foreach (var f in gifFrames) f.Item1.Dispose();
            gifFrames.Clear();
        }

        void GifReset() { st.Clear(); feedback = ""; helpShown = false; gifBoxOn = false; gifKey = gifMapFile = null; gifSplit = false; gifKeyL = gifTypedL = gifKeyR = gifTypedR = null; if (gifBox != null) gifBox.Hide(); }

        // Shift+T, escribir letra por letra, Enter: queda anotado
        void GifType(string text, Entry e)
        {
            gifKey = TypeKeyText; Shot(6);
            gifBox.Location = new Point(-6000, -6000);
            gifBox.Open(); gifBox.Location = new Point(-6000, -6000); gifBoxOn = true; gifKey = null; Shot(3);
            for (int i = 1; i <= text.Length; i++) { gifBox.SetText(text.Substring(0, i)); Application.DoEvents(); Shot(i == text.Length ? 12 : 2); }
            gifKey = "Enter"; Shot(4);
            gifBoxOn = false; gifBox.Hide(); gifKey = null;
            if (e != null) { string what = st.AddPicked(e, false); Feedback("⌨ " + what, Color.FromArgb(150, 220, 150), int.MaxValue / 2); }
        }

        void MakeGifs()
        {
            voice = false; // como la mayoria: sin Whisper (la ayuda no muestra los comandos de voz)
            string lang = Lang.En ? "en" : "es";
            gifBox = new TypeBox(this) { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000) };
            var noed = cat.Perks.First(p => p.Name.Contains("No One Escapes Death"));
            var pain = cat.Perks.First(p => p.Name.Contains("Pain Resonance"));
            var bbq = cat.Perks.First(p => p.Name.Contains("Barbecue"));

            // 1) anotar
            GifReset(); Shot(8);
            GifType("noed", noed); Shot(14); feedback = "";
            GifType("pain res", pain); Shot(22);
            GifSave("guia-" + lang + "-1");

            // 2) lo ven los cuatro: Meg anota y le aparece a Dwight; despues al reves
            GifReset(); gifSplit = true;
            gifL = CapOverlay(); gifR = CapOverlay(); Shot(8);
            gifKeyL = TypeKeyText; Shot(5); gifKeyL = null;
            foreach (var s in new[] { "n", "no", "noe", "noed" }) { gifTypedL = s; Shot(2); }
            Shot(6); gifKeyL = "Enter"; Shot(4); gifKeyL = null; gifTypedL = null;
            string w1 = st.AddPicked(noed, false);
            Feedback("⌨ " + w1, Color.FromArgb(150, 220, 150), int.MaxValue / 2); gifL = CapOverlay();
            Feedback("Meg: " + w1, Color.FromArgb(130, 190, 255), int.MaxValue / 2); gifR = CapOverlay();
            Shot(18);
            feedback = ""; gifL = CapOverlay(); gifR = CapOverlay(); Shot(4);
            gifKeyR = TypeKeyText; Shot(5); gifKeyR = null;
            foreach (var s in new[] { "b", "bb", "bbq" }) { gifTypedR = s; Shot(2); }
            Shot(6); gifKeyR = "Enter"; Shot(4); gifKeyR = null; gifTypedR = null;
            string w2 = st.AddPicked(bbq, false);
            Feedback("Dwight: " + w2, Color.FromArgb(130, 190, 255), int.MaxValue / 2); gifL = CapOverlay();
            Feedback("⌨ " + w2, Color.FromArgb(150, 220, 150), int.MaxValue / 2); gifR = CapOverlay();
            Shot(26);
            GifSave("guia-" + lang + "-2");

            // 3) mapa y ayuda
            GifReset(); st.AddPicked(noed, false); st.AddPicked(pain, false); Shot(6);
            var coal = cat.MapFiles.Keys.FirstOrDefault(k => k == "Coal Tower") ?? cat.MapFiles.Keys.First(k => k.Contains("Coal"));
            GifType(L("mapa coal", "map coal"), null);
            gifMapFile = cat.MapFiles[coal][0];
            for (int i = 0; i < 40 && MapImage(gifMapFile) == null; i++) { Application.DoEvents(); Thread.Sleep(250); }
            Shot(24);
            gifKey = HelpKeyName; Shot(3); gifMapFile = null; st.Clear(); feedback = ""; helpShown = true; Shot(34);
            helpShown = false; gifKey = null; Shot(6);
            GifSave("guia-" + lang + "-3");
        }

        // ---- rueda (prueba): alternativa a Shift+T con el mouse, como la rueda de comunicacion del juego.
        // Se usa con el cursor a la vista (menu ESC, lobby): categoria -> letra -> perk/addon/ofrenda/killer/mapa.
        // Es una ventana que recibe los clics SIN activarse (el juego no pierde el foco) y no intercepta nada.
        WheelWindow wheelWin;
        int vkWheel; bool wheelSh, wheelCt, wheelAl, wheelDown;
        int vkUndo; bool undoSh, undoCt, undoAl, undoDown;
        int vkBuild; bool buildSh, buildCt, buildAl, buildDown;
        volatile int vkMapOcr; bool mapOcrSh, mapOcrCt, mapOcrAl, mapOcrDown, mapOcrBusy;

        // ---- builds sugeridas (hens333.com): con el killer anotado, los lugares libres de perks muestran una build, atenuada.
        // Se elige sola la que mas coincide con lo anotado; la tecla (o la rueda) pasa a la siguiente; doble clic anota ese perk.
        bool BuildsOn { get { return cfg.Get("BuildsSugeridas", "no") == "si"; } }
        string buildsFor, buildsPerksKey = "";
        readonly List<List<Entry>> builds = new List<List<Entry>>();
        int buildPin = -1;
        bool buildsHidden;                                     // la tecla recorre build 1 -> build 2 -> sin build -> build 1
        readonly HashSet<Entry> ghostOff = new HashSet<Entry>(); // sugeridos sacados manteniendo apretado
        Dictionary<string, Entry> perkKeys; object perkKeysOf;
        void LoadBuilds()
        {
            HensBuilds.Init(Path.Combine(dir, "datos"));
            if (perkKeys == null || perkKeysOf != cat)
            {
                perkKeys = new Dictionary<string, Entry>(); perkKeysOf = cat;
                foreach (var p in cat.Perks) { string k = HensBuilds.Loose(p.Name); if (!perkKeys.ContainsKey(k)) perkKeys[k] = p; }
                foreach (var rn in Catalog.Renames) { Entry o; string nk = HensBuilds.Loose(rn[1]); if (perkKeys.TryGetValue(HensBuilds.Loose(rn[0]), out o) && !perkKeys.ContainsKey(nk)) perkKeys[nk] = o; }
                AddHensPerks();
            }
            string pk = string.Join("|", st.Perks.Select(p => p.Name));
            if (pk != buildsPerksKey) { buildsPerksKey = pk; buildPin = -1; } // se anoto algo: vuelve a elegir sola
            string slug = BuildsOn && st.Killer != null ? HensBuilds.Slug(st.Killer.Name) : null;
            if (slug == buildsFor) return;
            buildsFor = slug; buildPin = -1; builds.Clear(); buildsHidden = false; ghostOff.Clear();
            if (slug == null) return;
            var rows = HensBuilds.Get(slug, () => { try { BeginInvoke((Action)(() => { buildsFor = null; perkKeys = null; Relayout(); })); } catch { } });
            if (rows == null) return;
            if (perkKeys == null || perkKeysOf != cat)
            {
                perkKeys = new Dictionary<string, Entry>(); perkKeysOf = cat;
                foreach (var p in cat.Perks) { string k = HensBuilds.Loose(p.Name); if (!perkKeys.ContainsKey(k)) perkKeys[k] = p; }
                foreach (var rn in Catalog.Renames) { Entry o; string nk = HensBuilds.Loose(rn[1]); if (perkKeys.TryGetValue(HensBuilds.Loose(rn[0]), out o) && !perkKeys.ContainsKey(nk)) perkKeys[nk] = o; }
                AddHensPerks();
            }
            foreach (var f in rows)
            {
                var b = new List<Entry>();
                foreach (var s in f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Entry e;
                    if (perkKeys.TryGetValue(HensBuilds.Loose(s), out e) && !b.Contains(e)) b.Add(e);
                    else Log("[builds] perk de hens sin coincidencia: " + s, "");
                }
                if (b.Count > 0 && !builds.Any(x => x.SequenceEqual(b))) builds.Add(b);
            }
        }
        // perks de capitulos nuevos que los datos de la app todavia no tienen: se suman con el nombre de Hens (asi tambien se pueden anotar)
        void AddHensPerks()
        {
            foreach (var pi in HensBuilds.PerkInfo())
            {
                string k = HensBuilds.Loose(pi[1]);
                if (perkKeys.ContainsKey(k) || perkKeys.ContainsKey(HensBuilds.Loose(pi[0]))) continue;
                var e = new Entry { K = Kind.Perk, Name = pi[1], Owner = pi[2].StartsWith("General") ? "" : pi[2], Icon = "hens-" + pi[0] + ".png", Info = "", InfoEn = "" };
                e.AddKey(pi[1]);
                int ci = pi[1].IndexOf(':');
                if (ci > 0) e.AddKey(pi[1].Substring(ci + 1));
                cat.Perks.Add(e);
                perkKeys[k] = e;
            }
        }
        int CurBuild()
        {
            if (builds.Count == 0) return -1;
            if (buildPin >= 0 && buildPin < builds.Count) return buildPin;
            int best = 0, bc = -1;
            for (int i = 0; i < builds.Count; i++) { int c = builds[i].Count(st.Perks.Contains); if (c > bc) { bc = c; best = i; } }
            return best;
        }
        List<Entry> GhostPerks()
        {
            int bi = CurBuild();
            if (bi < 0 || buildsHidden || st.Perks.Count >= 4) return new List<Entry>();
            // primero los lugares (los primeros de la build que no estan anotados), despues se sacan los descartados: su lugar queda vacio
            return builds[bi].Where(p => !st.Perks.Contains(p)).Take(4 - st.Perks.Count).Where(p => !ghostOff.Contains(p)).ToList();
        }
        internal bool CanNextBuild { get { return BuildsOn && builds.Count > 0 && st.Perks.Count < 4; } }
        internal bool NextHidesBuild { get { return !buildsHidden && CurBuild() + 1 >= builds.Count; } } // lo que sigue es "sin build"
        internal void NextBuild()
        {
            if (!CanNextBuild) return;
            if (buildsHidden) { buildsHidden = false; buildPin = 0; }
            else if (NextHidesBuild) buildsHidden = true;
            else buildPin = CurBuild() + 1;
            ghostOff.Clear();
            Relayout();
        }
        // botones de builds en el hueco de abajo a la derecha del rombo (donde iria un quinto perk):
        // uno por build y el ultimo (✖) quita la sugerencia. Con el cursor libre se clickean.
        bool ShowBuildButtons { get { return BuildsOn && st.Killer != null && builds.Count > 0 && st.Perks.Count < 4; } }
        // boton redondo grande en el hueco de abajo a la derecha (el triangulo entre la esquina y los rombos): rota build 1 -> 2 -> sin build
        Rectangle RotateButton()
        {
            if (!ShowBuildButtons || cfg.Get("BotonBuild", "si") == "no") return Rectangle.Empty; // Configuracion: mostrar el boton
            var sl = items.Where(i => i.Slot).ToList();
            if (sl.Count < 4) return Rectangle.Empty;
            int D = sl[0].R.Width, ccx = sl[0].R.X + D / 2, ccy = sl[1].R.Y + D / 2, off = sl[2].R.X + D / 2 - ccx;
            // el triangulo libre de la esquina (su borde largo es la linea de los perks), achicado unos pixeles para que no se toquen.
            // Devuelve el cuadrado que lo contiene: el triangulo es la mitad de abajo a la derecha.
            int pct = Math.Max(30, Math.Min(100, cfg.GetInt("TamanoBotonBuild", 60)));   // Configuracion: tamaño del boton (se achica hacia la esquina)
            float L = (off + D / 2f - 7) * pct / 100f, cx = ccx + off + D / 2f - 1, cy = ccy + off + D / 2f - 1;
            return Rectangle.Round(new RectangleF(cx - L, cy - L, L, L));
        }
        // puntitos debajo del rombo: uno por build y uno (✖) por "sin build"; el encendido es lo que se ve
        List<Point> BuildDots()
        {
            var o = new List<Point>();
            if (!ShowBuildButtons) return o;
            var sl = items.Where(i => i.Slot).ToList();
            if (sl.Count < 4) return o;
            var low = sl[3].R;
            int n = builds.Count + 1, cx = low.X + low.Width / 2, cy = low.Bottom + 6;
            for (int k = 0; k < n; k++) o.Add(new Point(cx - (n - 1) * 5 + k * 10, cy));
            return o;
        }
        bool rotHover;
        bool RotateAt(Point p)
        {
            var b = RotateButton();
            if (b.IsEmpty) return false;
            return p.X <= b.Right + 3 && p.Y <= b.Bottom + 3 && (b.Right - p.X) + (b.Bottom - p.Y) <= b.Width + 3;
        }
        void PickBuild(int k)
        {
            if (k < 0 || k >= builds.Count) return;
            buildsHidden = false; buildPin = k; ghostOff.Clear();
            Relayout();
        }
        Entry GhostAt(Point p) { foreach (var it in items) if (it.Ghost && it.E != null && Hit(it, p)) return it.E; return null; }
        // deshacer lo ultimo (o el borrado de la lista)
        internal void UndoLast()
        {
            if (CanUndoClear) { UndoClear(); return; }
            if (!st.CanUndo) { Feedback(L("nada para deshacer", "nothing to undo"), Color.Silver, 2000); return; }
            BoxDo(L("deshacer", "undo"), () => { string w = st.Apply("deshacer"); int i = w.IndexOf(" · "); return i > 0 ? w.Substring(0, i) : w; }, "tecla");
        }
        volatile bool wheelOpen; // lo lee el gancho de teclas (otro hilo): con la rueda abierta, ESC la cierra

        public void ToggleWheel()
        {
            if (wheelWin != null && !wheelWin.IsDisposed && wheelWin.Visible) { wheelWin.Hide(); return; }
            if (DbdInFront() && !cursorShown)
            {
                Feedback(L("abrí el menú (ESC) para usar la rueda", "open the menu (ESC) to use the wheel"), Color.FromArgb(240, 170, 80), 3000);
                return;
            }
            if (wheelWin == null || wheelWin.IsDisposed) wheelWin = new WheelWindow(this);
            wheelWin.Open();
        }

        // ---- gamepad (opcional, apagado por defecto). Se LEE con Raw Input: Windows avisa cada informe del control,
        // sin consultar en bucle; no se bloquea ni se toca nada (el juego recibe todo igual).
        // Botones "canonicos" (bits): 0 Cuadrado/X 1 Cruz/A 2 Circulo/B 3 Triangulo/Y 4 L1 5 R1 6 L2 7 R2 8 Share/View 9 Options/Menu
        // 10 L3 11 R3 12 PS 13 Touchpad 14 arriba 15 derecha 16 abajo 17 izquierda
        static readonly string[] PadTok = { "Square", "Cross", "Circle", "Triangle", "L1", "R1", "L2", "R2", "Share", "Options", "L3", "R3", "PS", "Touchpad", "Up", "Right", "Down", "Left" };
        static readonly string[] PadPs = { "□", "✕", "○", "△", "L1", "R1", "L2", "R2", "Share", "Options", "L3", "R3", "PS", "Touchpad", "↑", "→", "↓", "←" };
        static readonly string[] PadXb = { "X", "A", "B", "Y", "LB", "RB", "LT", "RT", "View", "Menu", "LS", "RS", "Guide", "—", "↑", "→", "↓", "←" };
        PadReader pad;
        bool padXbox;
        uint padPrev, padRecMask;
        string padRecord;
        internal bool PadOn { get { return cfg.Get("Gamepad", "no") == "si"; } }
        internal void UpdatePad()
        {
            if (PadOn && pad == null) pad = new PadReader(this);
            else if (!PadOn && pad != null) { pad.Close(); pad = null; }
        }
        internal uint PadMask(string key, string def)
        {
            uint m = 0;
            foreach (var t in cfg.Get(key, def).Split('+')) { int i = Array.FindIndex(PadTok, x => x.Equals(t.Trim(), StringComparison.OrdinalIgnoreCase)); if (i >= 0) m |= 1u << i; }
            return m;
        }
        internal string PadText(uint m)
        {
            var n = padXbox ? PadXb : PadPs;
            var parts = Enumerable.Range(0, PadTok.Length).Where(i => (m & (1u << i)) != 0).Select(i => n[i]).ToList();
            return parts.Count > 0 ? string.Join(" + ", parts) : L("sin botón", "no button");
        }
        internal void PadRecord(string key)
        {
            if (pad == null) { Feedback(L("primero activá \"Usar la rueda con gamepad\"", "first turn on \"Use the wheel with a gamepad\""), Color.FromArgb(240, 170, 80), 4000); return; }
            padRecord = key; padRecMask = 0;
            Feedback(L("apretá en el control el botón (o la combinación) y soltá…", "press the button (or combination) on the controller and release…"), Color.FromArgb(130, 190, 255), 8000);
        }
        // un informe del control (ya traducido a botones canonicos y stick derecho -1..1)
        void OnPad(uint bits, float rx, float ry, bool xbox)
        {
            padXbox = xbox;
            bool padWheel = wheelWin != null && !wheelWin.IsDisposed && wheelWin.Visible && wheelWin.Pad;
            if (bits == padPrev && !padWheel) return; // nada nuevo (la mayoria de los informes): no se hace nada
            uint pressed = bits & ~padPrev;
            if (padRecord != null)
            {
                if (bits != 0) padRecMask |= bits;
                else if (padRecMask != 0)
                {
                    SetCfg(padRecord, string.Join("+", Enumerable.Range(0, PadTok.Length).Where(i => (padRecMask & (1u << i)) != 0).Select(i => PadTok[i])));
                    Feedback(L("listo: ", "done: ") + PadText(padRecMask), Color.FromArgb(150, 220, 150), 3000);
                    padRecord = null; padRecMask = 0;
                    if (settingsWin != null && !settingsWin.IsDisposed) settingsWin.Refresh2();
                }
                padPrev = bits;
                return;
            }
            uint open = PadMask("PadAbrir", "L2"), pick = PadMask("PadElegir", "R2");
            bool held = open != 0 && (bits & open) == open, was = open != 0 && (padPrev & open) == open;
            padPrev = bits;
            if (held && !was && (!KeysOnlyInDbd || DbdInFront()))
            {
                if (wheelWin == null || wheelWin.IsDisposed) wheelWin = new WheelWindow(this);
                wheelWin.Open(true, true);
                padWheel = true;
            }
            else if (!held && was && padWheel) { wheelWin.Hide(); return; } // soltar cierra
            if (!held || !padWheel) return;
            wheelWin.PadAim(rx, ry);
            if (pick != 0 && (pressed & pick) != 0 && (bits & pick) == pick) wheelWin.PadPick();
        }

        class PadReader : NativeWindow
        {
            readonly Overlay ov;
            [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
            [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, WParam; }
            [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
            [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);
            [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, IntPtr data, ref uint size);
            [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, IntPtr caps);
            [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort len, IntPtr pp);
            [DllImport("hid.dll")] static extern int HidP_GetUsages(int type, ushort page, ushort link, ushort[] list, ref uint len, IntPtr pp, IntPtr report, uint reportLen);
            [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr pp, IntPtr report, uint reportLen);
            const int Ok = 0x00110000;
            class Dev { public int Vid, Pid; public IntPtr Pp; public Dictionary<ushort, int[]> Range = new Dictionary<ushort, int[]>(); }
            readonly Dictionary<IntPtr, Dev> devs = new Dictionary<IntPtr, Dev>();
            IntPtr buf = Marshal.AllocHGlobal(2048);
            readonly ushort[] usages = new ushort[64];
            readonly uint hs = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));

            public PadReader(Overlay o)
            {
                ov = o;
                CreateHandle(new CreateParams());
                Register(true);
            }
            void Register(bool on)
            {
                // 4 = joystick, 5 = gamepad (los de PlayStation y Xbox se anuncian como gamepad)
                var d = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = on ? 0x100u : 0x1u, Target = on ? Handle : IntPtr.Zero },
                                new RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = on ? 0x100u : 0x1u, Target = on ? Handle : IntPtr.Zero } };
                RegisterRawInputDevices(d, 2, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            public void Close()
            {
                Register(false);
                DestroyHandle();
                foreach (var d in devs.Values) if (d.Pp != IntPtr.Zero) Marshal.FreeHGlobal(d.Pp);
                devs.Clear();
                if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0xFF && buf != IntPtr.Zero) { try { OnInput(m.LParam); } catch { } }
                base.WndProc(ref m);
            }
            Dev GetDev(IntPtr h)
            {
                Dev d;
                if (devs.TryGetValue(h, out d)) return d;
                d = new Dev();
                IntPtr info = Marshal.AllocHGlobal(32);
                try
                {
                    Marshal.WriteInt32(info, 32);
                    uint sz = 32;
                    if (GetRawInputDeviceInfo(h, 0x2000000b, info, ref sz) != unchecked((uint)-1)) { d.Vid = Marshal.ReadInt32(info, 8); d.Pid = Marshal.ReadInt32(info, 12); }
                }
                finally { Marshal.FreeHGlobal(info); }
                uint ps = 0;
                GetRawInputDeviceInfo(h, 0x20000005, IntPtr.Zero, ref ps);
                if (ps > 0 && ps < 1 << 20)
                {
                    d.Pp = Marshal.AllocHGlobal((int)ps);
                    if (GetRawInputDeviceInfo(h, 0x20000005, d.Pp, ref ps) == unchecked((uint)-1)) { Marshal.FreeHGlobal(d.Pp); d.Pp = IntPtr.Zero; }
                }
                if (d.Pp != IntPtr.Zero)
                {
                    // rangos de los ejes (cada control declara los suyos)
                    IntPtr caps = Marshal.AllocHGlobal(64);
                    try
                    {
                        if (HidP_GetCaps(d.Pp, caps) == Ok)
                        {
                            ushort n = (ushort)Marshal.ReadInt16(caps, 48);
                            if (n > 0 && n < 256)
                            {
                                IntPtr vc = Marshal.AllocHGlobal(n * 72);
                                try
                                {
                                    if (HidP_GetValueCaps(0, vc, ref n, d.Pp) == Ok)
                                        for (int i = 0; i < n; i++)
                                        {
                                            IntPtr c = vc + i * 72;
                                            if ((ushort)Marshal.ReadInt16(c, 0) != 1) continue;
                                            int bits = (ushort)Marshal.ReadInt16(c, 18), lo = Marshal.ReadInt32(c, 40), hi = Marshal.ReadInt32(c, 44);
                                            if (hi <= lo) { lo = 0; hi = bits >= 31 ? int.MaxValue : (1 << bits) - 1; }
                                            ushort u0 = (ushort)Marshal.ReadInt16(c, 56), u1 = Marshal.ReadByte(c, 12) != 0 ? (ushort)Marshal.ReadInt16(c, 58) : u0;
                                            for (ushort u = u0; u <= u1 && u < 0x40; u++) d.Range[u] = new[] { lo, hi };
                                        }
                                }
                                finally { Marshal.FreeHGlobal(vc); }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(caps); }
                }
                devs[h] = d;
                return d;
            }
            void OnInput(IntPtr h)
            {
                uint size = 0;
                GetRawInputData(h, 0x10000003, IntPtr.Zero, ref size, hs);
                if (size == 0 || size > 2048) return;
                if (GetRawInputData(h, 0x10000003, buf, ref size, hs) != size) return;
                if (Marshal.ReadInt32(buf) != 2) return; // RIM_TYPEHID
                IntPtr dev = Marshal.ReadIntPtr(buf, 8);
                uint rs = (uint)Marshal.ReadInt32(buf, (int)hs), cnt = (uint)Marshal.ReadInt32(buf, (int)hs + 4);
                if (rs == 0 || cnt == 0 || hs + 8 + rs * cnt > size) return;
                IntPtr rep = buf + (int)(hs + 8 + rs * (cnt - 1)); // el ultimo informe es el estado actual
                var d = GetDev(dev);
                uint bits; float rx, ry;
                if (d.Vid == 0x054C ? !ParseSony(d, rep, rs, out bits, out rx, out ry) : !ParseHid(d, rep, rs, out bits, out rx, out ry)) return;
                ov.OnPad(bits, rx, ry, d.Vid == 0x045E);
            }
            static uint Dpad(int v)
            {
                uint b = 0;
                if (v == 7 || v == 0 || v == 1) b |= 1u << 14;
                if (v == 1 || v == 2 || v == 3) b |= 1u << 15;
                if (v == 3 || v == 4 || v == 5) b |= 1u << 16;
                if (v == 5 || v == 6 || v == 7) b |= 1u << 17;
                return b;
            }
            static float Ax(int v) { return (v - 127.5f) / 127.5f; }
            // PlayStation: se leen los bytes del informe (por Bluetooth el control manda un formato propio que el descriptor no explica)
            static bool ParseSony(Dev d, IntPtr r, uint len, out uint bits, out float rx, out float ry)
            {
                bits = 0; rx = ry = 0;
                int id = Marshal.ReadByte(r), b; bool dualSense = d.Pid == 0x0CE6 || d.Pid == 0x0DF2;
                int sticks, btn;
                if (dualSense && id == 0x01 && len >= 64) { b = 1; sticks = b; btn = b + 7; }        // DualSense por cable
                else if (dualSense && id == 0x31 && len >= 12) { b = 2; sticks = b; btn = b + 7; }   // DualSense por Bluetooth
                else if (id == 0x11 && len >= 12) { b = 3; sticks = b; btn = b + 4; }                // DS4 por Bluetooth
                else if (id == 0x01 && len >= 8) { b = 1; sticks = b; btn = b + 4; }                 // DS4 por cable / modo basico
                else return false;
                int b0 = Marshal.ReadByte(r, btn), b1 = Marshal.ReadByte(r, btn + 1), b2 = Marshal.ReadByte(r, btn + 2);
                bits = (uint)((b0 >> 4) & 0xF) | (uint)b1 << 4 | (uint)(b2 & 0x3) << 12 | Dpad(b0 & 0xF);
                rx = Ax(Marshal.ReadByte(r, sticks + 2)); ry = Ax(Marshal.ReadByte(r, sticks + 3));
                return true;
            }
            float Val(Dev d, ushort u, IntPtr r, uint len, out bool ok)
            {
                uint v = 0; int[] rg;
                ok = d.Range.TryGetValue(u, out rg) && HidP_GetUsageValue(0, 1, 0, u, out v, d.Pp, r, len) == Ok;
                if (!ok) return 0;
                return Math.Max(-1f, Math.Min(1f, ((long)v - rg[0]) * 2f / Math.Max(1L, (long)rg[1] - rg[0]) - 1f));
            }
            // el resto (Xbox, genericos): lo que declara el descriptor HID
            bool ParseHid(Dev d, IntPtr r, uint len, out uint bits, out float rx, out float ry)
            {
                bits = 0; rx = ry = 0;
                if (d.Pp == IntPtr.Zero) return false;
                uint n = (uint)usages.Length;
                if (HidP_GetUsages(0, 9, 0, usages, ref n, d.Pp, r, len) != Ok) n = 0;
                bool xbox = d.Vid == 0x045E;
                int[] xmap = { 1, 2, 0, 3, 4, 5, 8, 9, 10, 11 }; // A B X Y LB RB View Menu LS RS
                for (int i = 0; i < n; i++)
                {
                    int u = usages[i];
                    if (xbox) { if (u >= 1 && u <= xmap.Length) bits |= 1u << xmap[u - 1]; }
                    else if (u >= 1 && u <= 14) bits |= 1u << (u - 1);
                }
                bool ok;
                uint hv;
                int[] hr;
                if (d.Range.TryGetValue(0x39, out hr) && HidP_GetUsageValue(0, 1, 0, 0x39, out hv, d.Pp, r, len) == Ok) bits |= Dpad((int)hv - hr[0]);
                // stick derecho: Rx/Ry (Xbox) o Z/Rz (la mayoria de los genericos)
                rx = Val(d, 0x33, r, len, out ok); bool okx = ok;
                ry = Val(d, 0x34, r, len, out ok);
                if (!okx || !ok || !xbox && d.Range.ContainsKey(0x35)) { rx = Val(d, 0x32, r, len, out ok); ry = Val(d, 0x35, r, len, out ok); }
                // gatillos del Xbox por HID: un solo eje (Z) compartido
                if (xbox) { float z = Val(d, 0x32, r, len, out ok); if (ok && z > 0.5f) bits |= 1u << 6; if (ok && z < -0.5f) bits |= 1u << 7; }
                return true;
            }
        }

        void WheelSwap(Entry old, Entry neu)
        {
            EndSample();
            var before = st.Snapshot();
            string what = st.ReplacePicked(old, neu, false);
            bool ok = !Lang.Fail(what);
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Log("[rueda] " + old.Name + " -> " + neu.Name, what);
            Feedback("◈ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }
        void WheelPick(Entry e)
        {
            if (e.Action != null) { DoAction(e.Action); return; }
            EndSample();
            var before = st.Snapshot();
            string what = st.AddPicked(e, false);
            if (st.PendingKiller != null) what = st.Apply("si"); // en la rueda elegir otro killer ya es la confirmacion
            bool ok = !Lang.Fail(what);
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Log("[rueda] " + e.Name, what);
            Feedback("◈ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }

        class WheelWindow : Form
        {
            class Seg { public string Label, Big, Small, Icon, Glyph; public Entry E; public Action Go; public bool Marked, Leaf; public int Group = -1; }
            class Level { public string Title; public List<Seg> Segs = new List<Seg>(); public Entry Swap; } // Swap: lo que entra (rueda de "cual sale")

            readonly Overlay ov;
            readonly List<Level> stack = new List<Level>();
            int hover = -1; // -1 nada, -2 el centro
            const int D = 580, R = 278, Rin = 96, InfoH = 116; // InfoH: debajo de la rueda, que hace lo que tenes bajo el mouse
            static readonly Color Key = Color.FromArgb(1, 0, 1);

            public WheelWindow(Overlay o)
            {
                ov = o;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
                BackColor = Key; TransparencyKey = Key; Opacity = 0.95; DoubleBuffered = true;
                ClientSize = new Size(D, D + InfoH);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override void OnVisibleChanged(EventArgs e)
            {
                base.OnVisibleChanged(e);
                ov.wheelOpen = Visible;
                if (!Visible) { steering = false; RawMouse(false); }
            }

            // ---- modo "como el juego": mantener la tecla y mover el mouse apunta; soltar elige. El movimiento se LEE con
            // Raw Input (RIDEV_INPUTSINK): no se bloquea ni se toca nada, el juego lo sigue recibiendo igual.
            public bool Gesture, ClickMode;
            bool steering, rawOn;
            float vx, vy;
            const float SteerMin = 40f;
            float SteerMax { get { return ClickMode ? 140f : 300f; } } // con clic, corto (cambiar de direccion es rapido); empujando, largo (para no entrar sin querer)
            [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
            [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, WParam; }
            [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
            [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint headerSize);
            [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
            [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
            void RawMouse(bool on)
            {
                if (on == rawOn) return;
                var d = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = on ? 0x100u : 0x1u, Target = on ? Handle : IntPtr.Zero } };
                if (RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) rawOn = on;
            }
            public void Steer(bool down)
            {
                if (down) { steering = true; vx = vy = 0; if (hover != -1) { hover = -1; Invalidate(); } return; }
                if (!steering) return;
                steering = false;
                int h = hover;
                vx = vy = 0; hover = -1;
                if (ClickMode) { Hide(); return; }                    // con clic: soltar la tecla cierra sin elegir
                if (h >= 0 && h < Cur.Segs.Count) Cur.Segs[h].Go(); // soltar elige (si es una categoria, entra y espera la tecla)
                else Hide();                                          // soltar sin apuntar: se cierra
                if (Visible) Invalidate();
            }
            void OnRawInput(IntPtr h)
            {
                if (!steering) return;
                uint hs = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)), size = 0;
                GetRawInputData(h, 0x10000003, IntPtr.Zero, ref size, hs);
                if (size == 0 || size > 256) return;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (GetRawInputData(h, 0x10000003, buf, ref size, hs) != size) return;
                    if (Marshal.ReadInt32(buf) != 0) return;                       // RIM_TYPEMOUSE
                    // botones (solo se leen; el juego los recibe igual): derecho = atras; izquierdo = elegir (modo clic)
                    int bf = Marshal.ReadInt16(buf, (int)hs + 4) & 0xFFFF;
                    if ((bf & 0x4) != 0) { vx = vy = 0; hover = -1; Back(); if (Visible) Invalidate(); return; }
                    if ((bf & 0x1) != 0 && ClickMode)
                    {
                        int h0 = hover;
                        vx = vy = 0; hover = -1;
                        if (h0 >= 0 && h0 < Cur.Segs.Count) Cur.Segs[h0].Go();
                        if (Visible) Invalidate();
                        return;
                    }
                    if ((Marshal.ReadInt16(buf, (int)hs) & 1) != 0) return;        // movimiento absoluto (tableta): no
                    Move(Marshal.ReadInt32(buf, (int)hs + 12), Marshal.ReadInt32(buf, (int)hs + 16));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            void Move(int dx, int dy)
            {
                {
                    vx += dx; vy += dy;
                    float len = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (len > SteerMax) { vx = vx * SteerMax / len; vy = vy * SteerMax / len; len = SteerMax; }
                    int nh = -1;
                    if (len >= SteerMin)
                    {
                        int n = Math.Max(1, Cur.Segs.Count);
                        float per = 360f / n;
                        double ang = Math.Atan2(vy, vx) * 180 / Math.PI + 90 + per / 2;
                        ang = ((ang % 360) + 360) % 360;
                        nh = Math.Min(n - 1, (int)(ang / per));
                    }
                    if (nh != hover) { hover = nh; Invalidate(); }
                    else if (ov.cfg.Get("RuedaPuntero", "no") == "si") { pointerOnly = true; Invalidate(); }
                    // empujar hasta el borde sobre una categoria: entra sin soltar (asi se llega al final con una sola apretada)
                    if (!ClickMode && nh >= 0 && len >= SteerMax * 0.98f && IsSub(Cur.Segs[nh]))
                    {
                        vx = vy = 0; hover = -1;
                        Cur.Segs[nh].Go();
                        Invalidate();
                    }
                }
            }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x8; return cp; } // sin activarse, sin barra de tareas, arriba
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE: el clic llega, el foco queda en el juego
                if (m.Msg == 0xFF) OnRawInput(m.LParam);                // WM_INPUT (modo gesto)
                base.WndProc(ref m);
            }

            public bool Pad;
            // gamepad: el stick derecho apunta (posicion absoluta, con un margen al centro); el boton de elegir elige o, al centro, vuelve
            public void PadAim(float x, float y)
            {
                float mag = (float)Math.Sqrt(x * x + y * y);
                int nh = hover;
                if (mag < (hover >= 0 ? 0.35f : 0.55f)) nh = -1;
                else
                {
                    int n = Math.Max(1, Cur.Segs.Count);
                    float per = 360f / n;
                    double ang = Math.Atan2(y, x) * 180 / Math.PI + 90 + per / 2;
                    ang = ((ang % 360) + 360) % 360;
                    nh = Math.Min(n - 1, (int)(ang / per));
                }
                if (nh != hover) { hover = nh; Invalidate(); }
            }
            public void PadPick()
            {
                int h = hover;
                hover = -1;
                if (h >= 0 && h < Cur.Segs.Count) Cur.Segs[h].Go(); else Back();
                if (Visible) Invalidate();
            }
            public void Open(bool gesture = false, bool pad = false)
            {
                Gesture = gesture;
                Pad = pad;
                if (pad) ClickMode = true;
                steering = false; vx = vy = 0;
                // en modo gesto la ventana no recibe clics (pasan al juego): todo se maneja con la tecla y el movimiento
                int ex = GetWindowLong(Handle, -20);
                SetWindowLong(Handle, -20, gesture ? ex | 0x20 : ex & ~0x20);
                RawMouse(gesture && !pad);
                stack.Clear();
                stack.Add(Root());
                hover = -1;
                var scr = Screen.PrimaryScreen.Bounds;
                Location = new Point(scr.Left + (scr.Width - D) / 2, scr.Top + (scr.Height - D) / 2);
                Show();
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
                Invalidate();
            }

            // nombre para ordenar y para la letra: sin "Hex:", "The", etc.
            string SortName(Entry e)
            {
                string n = ov.DName(e);
                n = Regex.Replace(n, @"^(?:(?:Hex|Scourge Hook|Boon|Invocation|Maleficio|Gancho de castigo|Bendición|Invocación)\s*:\s*|(?:The|El|La|Los|Las)\s+)", "", RegexOptions.IgnoreCase);
                return n.Trim();
            }
            static int RarRank(string r)
            {
                switch (r) { case "iridescent": case "visceral": return 0; case "veryrare": return 1; case "rare": return 2; case "uncommon": return 3; case "common": return 4; default: return 5; }
            }
            // color de rareza (addons y ofrendas), como el fondo de los iconos en el juego
            // tiers de perks (S mejor ... D peor), parche 10.2.2a/b (noviembre 2026). Base: timesaver.gg/blog/dbd-best-killer-perks-tier-list-2026
            // (criterio del autor contrastado con NightLight y BHVR). Lo que esa lista no tiene se ubica por cuantas builds de
            // hens333.com/killerbuilds lo usan (10 o mas: A; 5 a 9: B; menos: C). Lo que no esta en ningun lado es D.
            // Los renombrados (licencias que se fueron) van con los dos nombres.
            static readonly string[][] PerkTierList = {
                new[] { "S", "Pain Resonance", "Corrupt Intervention", "Pop Goes the Weasel", "Grim Embrace", "Lethal Pursuer", "Nowhere to Hide" },
                new[] { "A", "Help Wanted", "Undone", "Eruption", "Ruin", "Dead Mans Switch", "Surge", "Unrelenting",
                        "Keep Them Waiting", "Save the Best for Last", "No Holds Barred", "Deadlock", "Bamboozle",
                        "Weeping Wounds", "Gift of Pain", "Turn Back the Clock", "Spies from the Shadows", "Fortunes Fool", "Plaything" },
                new[] { "B", "Monstrous Shrine", "Machine Learning", "Ravenous", "Distressing", "Barbecue & Chili", "Darkness Revealed",
                        "Bitter Murmur", "Knock Out", "A Nurses Calling", "Friends til the End", "One Track Mind", "Brutal Strength",
                        "Sloppy Butcher", "Enduring", "No Way Out", "Agitation", "Whispers", "Lay Waste", "Tinkerer" },
                new[] { "C", "Thrill of the Hunt", "Blood Favor", "Dissolution", "Dark Arrogance", "Shattered Hope", "Dominance", "Insidious",
                        "Nothing but Misery", "Deathbound", "Predator", "Territorial Imperative",
                        "Thanatophobia", "See How They Run", "Play With Your Food", "Devour Hope", "Genetic Limits", "Dragons Grip", "Deerstalker",
                        "Unforeseen", "Starstruck", "Secret Project", "Remember Me", "Rapid Brutality", "Rancor", "Phantom Fear", "No Quarter",
                        "Mindbreaker", "Leverage", "Languid Touch", "Infectious Fright", "Wretched Fate", "Pentimento", "Hive Mind",
                        "Haunted Ground", "Crowd Control", "Forever Entwined", "Discordance", "Coup de Grace", "Batteries Included", "Gaining Steam" } };
            // perks que practicamente no se ven en partidas (opinion del meta, fija). Solo se separan en categorias de mas de 12.
            static readonly string[] RarePerks = {
                "Huntress Lullaby", "Overture of Doom", "Under Your Thumb", "Scared to Death",
                "Game Afoot",
                "Shattered Hope", "Celestial Witness", "Territorial Imperative", "Human Greed", "Dominance", "Zanshin Tactics",
                "Awakened Awareness", "Im All Ears", "Overwhelming Presence", "Predator", "Nemesis",
                "Superior Anatomy", "Dark Arrogance", "All Shaking Thunder", "Rampage", "Monitor & Abuse", "Shadowborn", "Whispers", "Unbound",
                "Beast of Prey", "Insidious", "Languid Touch", "Hubris", "Blood Echo", "Forced Penance" };
            static HashSet<string> rarePerks;
            static bool IsRarePerk(Entry e)
            {
                if (rarePerks == null) rarePerks = new HashSet<string>(RarePerks.Select(Bare));
                if (HensBuilds.IsUsed(e.Name)) return false;
                return rarePerks.Contains(Bare(Regex.Replace(e.Name, @"^(Hex|Scourge Hook|Boon|Invocation):\s*", "")));
            }
            static Dictionary<string, string> perkTiers;
            public static string PerkTier(Entry e)
            {
                if (e == null || e.K != Kind.Perk) return null;
                if (perkTiers == null)
                {
                    perkTiers = new Dictionary<string, string>();
                    foreach (var t in PerkTierList) for (int i = 1; i < t.Length; i++) perkTiers[Bare(t[i])] = t[0];
                }
                string v;
                return perkTiers.TryGetValue(Bare(Regex.Replace(e.Name, @"^(Hex|Scourge Hook|Boon|Invocation):\s*", "")), out v) ? v : "D";
            }
            public static bool ShowTiers = true; // Configuracion > Rueda
            internal static Color TierColor(string t)
            {
                switch (t)
                {
                    // los colores de rareza del juego: S iridiscente, A muy raro (violeta), B raro (verde), C poco comun (amarillo), D comun (marron)
                    case "S": return RarityColor("visceral");
                    case "A": return RarityColor("veryrare");
                    case "B": return RarityColor("rare");
                    case "C": return RarityColor("uncommon");
                    default: return RarityColor("common");
                }
            }
            static Color? RarColor(Entry e)
            {
                if (e != null && e.K == Kind.Perk) return ShowTiers ? TierColor(PerkTier(e)) : (Color?)null;
                if (e == null || (e.K != Kind.Addon && e.K != Kind.Offering)) return null;
                switch (e.Rarity)
                {
                    case "common": case "uncommon": case "rare": case "veryrare": case "visceral": return RarityColor(e.Rarity);
                    case "iridescent": return RarityColor("visceral");
                    default: return null;
                }
            }
            // lo que hace cada ofrenda, en una linea y desde el lado del sobreviviente (la descripcion del juego es larga)
            static readonly Dictionary<string, string[]> Brief = new Dictionary<string, string[]>
            {
                { "Map offering", new[] { "El killer eligió este mapa.", "The killer picked this map." } },
                { "Fog offering", new[] { "Más niebla.", "More fog." } },
                { "Hook offering", new[] { "Ganchos más cerca entre sí.", "Hooks closer together." } },
                { "Chest offering", new[] { "Menos cofres.", "Fewer chests." } },
                { "Cypress Memento Mori", new[] { "Puede hacer mori al último sobreviviente.", "Can mori the last survivor." } },
                { "Ivory Memento Mori", new[] { "Puede hacer mori a 1 sobreviviente que esté en su 2.º gancho.", "Can mori 1 survivor on their 2nd hook stage." } },
                { "Ebony Memento Mori", new[] { "Puede hacer mori a todos los que estén en su 2.º gancho.", "Can mori anyone on their 2nd hook stage." } },
                { "Moldy Oak", new[] { "Ganchos un poco más cerca entre sí.", "Hooks slightly closer together." } },
                { "Rotten Oak", new[] { "Ganchos más cerca entre sí.", "Hooks closer together." } },
                { "Putrid Oak", new[] { "Ganchos mucho más cerca entre sí.", "Hooks much closer together." } },
                { "Scratched Coin", new[] { "1 cofre menos.", "1 fewer chest." } },
                { "Cut Coin", new[] { "2 cofres menos.", "2 fewer chests." } },
                { "Clear Reagent", new[] { "Un poco menos de niebla.", "Slightly less fog." } },
                { "Faint Reagent", new[] { "Un poco más de niebla.", "Slightly more fog." } },
                { "Hazy Reagent", new[] { "Más niebla.", "More fog." } },
                { "Murky Reagent", new[] { "Mucha más niebla.", "Much more fog." } },
                { "Bloodied Blueprint", new[] { "Sótano en la cabaña del killer.", "Basement in the Killer Shack." } },
                { "Torn Blueprint", new[] { "Sótano en el edificio principal.", "Basement in the main building." } },
                { "Annotated Blueprint", new[] { "Trampilla en la cabaña del killer.", "Hatch in the Killer Shack." } },
                { "Vigo’s Blueprint", new[] { "Trampilla en el edificio principal.", "Hatch in the main building." } },
                { "Vigo's Blueprint", new[] { "Trampilla en el edificio principal.", "Hatch in the main building." } },
                { "Black Ward", new[] { "El killer no gasta sus addons.", "The killer keeps their add-ons." } },
                { "Shroud of Vanishing", new[] { "Anula los shrouds: cada sobreviviente aparece donde le toque.", "Cancels survivor shrouds: everyone spawns normally." } },
            };
            // perks: una linea, lo que te hace a vos (escrito a mano; la descripcion del juego es demasiado larga). Clave: el nombre sin signos (Bare)
            static readonly string[][] PerkBriefList = {
                new[] { "Agitation", "Mientras te carga: +18 % Celeridad y radio de terror 12 m más grande.", "While carrying: +18% Haste and 12 m bigger Terror Radius." },
                new[] { "Bamboozle", "Bloquea la ventana que salta (16 s) y salta ventanas 15 % más rápido.", "Blocks the window it vaults (16 s) and vaults 15% faster." },
                new[] { "Barbecue & Chili", "Al colgar a alguien, ve las auras de los que estén a más de 40 m (5 s).", "On hook, sees the Auras of everyone farther than 40 m (5 s)." },
                new[] { "Beast of Prey", "Al conseguir sed de sangre: Indetectable (40 s). No escuchás el radio.", "When it gets Bloodlust: Undetectable (40 s). No Terror Radius." },
                new[] { "Bitter Murmur", "Gen terminado: ve a los que estén a 20 m (10 s). Con el último, a todos (20 s).", "Gen done: sees those within 20 m (10 s). Last gen: everyone (20 s)." },
                new[] { "Blood Echo", "Al colgar: todos los heridos con Agotamiento y Hemorragia (30 s).", "On hook: all injured get Exhausted and Hemorrhage (30 s)." },
                new[] { "Bloodhound", "Los charcos de sangre son más brillantes y duran 4 s más: rastrea heridos.", "Blood pools are brighter and last 4 s longer: tracks the injured." },
                new[] { "Blood Warden", "Con una puerta abierta, al colgar: las salidas se bloquean (60 s). Una vez.", "Once a gate is open, on hook: exits blocked (60 s). Once." },
                new[] { "Shattered Hope", "Destruye los tótems benefactores. Bendecir o limpiar bloquea tótems (20 s).", "Destroys Boon Totems. Blessing or cleansing blocks totems (20 s)." },
                new[] { "Brutal Strength", "Rompe palés y paredes, y patea gens 20 % más rápido.", "Breaks pallets and walls, and kicks gens 20% faster." },
                new[] { "Corrupt Intervention", "Al empezar: los 3 gens más lejos de él, bloqueados 120 s o hasta un derribo.", "At start: the 3 gens farthest from it are blocked 120 s or until a down." },
                new[] { "Coulrophobia", "En su radio de terror curás 30 % más lento y las pruebas giran más rápido.", "In its Terror Radius you heal 30% slower and skill checks spin faster." },
                new[] { "Cruel Limits", "Gen terminado: todas las ventanas bloqueadas (30 s) y las ve.", "Gen done: all windows blocked (30 s) and it sees them." },
                new[] { "Dark Devotion", "Si golpea a la obsesión: ella lleva el radio de terror y él queda Indetectable (45 s).", "Obsession hit: they carry the Terror Radius, killer is Undetectable (45 s)." },
                new[] { "Dead Mans Switch", "Tras colgar: el próximo que suelte un gen más de 2 s lo deja bloqueado (40 s).", "After a hook: the next one to leave a gen for 2 s gets it blocked (40 s)." },
                new[] { "Deathbound", "Al curar a otro gritás; después, lejos de él: Inconsciencia y ves su aura.", "Healing another makes you scream; then, away from them: Oblivious." },
                new[] { "Deerstalker", "Cada 30 s, el que menos persiguió ve su aura y el killer ve la de él (4 s).", "Every 30 s, the least-chased sees its Aura and it sees theirs (4 s)." },
                new[] { "Discordance", "Si 2 o más reparan el mismo gen, le avisa y ve el gen.", "When 2 or more repair the same gen, it's alerted and sees the gen." },
                new[] { "Distressing", "Radio de terror 30 % más grande; dentro reparás 8 % más lento.", "Terror Radius 30% bigger; inside it you repair 8% slower." },
                new[] { "Dragons Grip", "Tras patear un gen, el primero que lo repare grita y queda con Vulnerabilidad (60 s).", "After a kick, the first to repair that gen screams and gets Exposed (60 s)." },
                new[] { "Dying Light", "Por cada gancho que no sea la obsesión, los demás curan y reparan 3 % más lento.", "Each non-Obsession hook: others heal and repair 3% slower." },
                new[] { "Enduring", "Los aturdimientos con palé le duran 50 % menos.", "Pallet stuns are 50% shorter." },
                new[] { "Fire Up", "Por cada gen terminado: rompe, patea, salta y levanta 8 % más rápido (hasta 5).", "Each gen done: breaks, kicks, vaults and picks up 8% faster (up to 5)." },
                new[] { "Forced Penance", "El que recibe un golpe por proteger a otro: Desesperanza (80 s).", "Whoever takes a protection hit: Broken (80 s)." },
                new[] { "Franklins Demise", "Al golpearte se te cae el objeto, y ve los objetos tirados.", "Hits make you drop your item, and it sees dropped items." },
                new[] { "Furtive Chase", "Al colgar a la obsesión: Celeridad e Indetectable (18 s). Quien la saque pasa a ser la obsesión.", "Hooking the Obsession: Haste and Undetectable (18 s). The rescuer becomes Obsession." },
                new[] { "Gearhead", "Tras golpear a alguien (30 s), una prueba buena en un gen te revela (8 s).", "After a hit (30 s), a good skill check on a gen reveals you (8 s)." },
                new[] { "Overcharge", "Gen pateado: retrocede cada vez más rápido y el que lo toca recibe una prueba difícil.", "Kicked gen: regresses faster and faster; the next to touch it gets a hard check." },
                new[] { "Hangmans Trick", "4 ganchos de castigo. Cargando, ve a los que estén cerca de ellos; avisa si sabotean.", "4 Scourge Hooks. Carrying, sees those near them; alerted on sabotage." },
                new[] { "Devour Hope", "Desenganches con él lejos dan fichas: Celeridad, luego Vulnerabilidad, luego mori.", "Unhooks while it's far give tokens: Haste, then Exposed, then mori." },
                new[] { "Haunted Ground", "2 tótems: al limpiar uno, todos con Vulnerabilidad (60 s).", "2 totems: cleansing one gives everyone Exposed (60 s)." },
                new[] { "Huntress Lullaby", "Con cada gancho, las pruebas avisan más tarde y son más chicas; fallar saca 6 % más.", "Each hook: later skill check warnings, smaller zones; misses cost 6% more." },
                new[] { "Ruin", "Los gens que nadie repara retroceden solos al 150 %.", "Gens nobody is repairing regress on their own at 150%." },
                new[] { "The Third Seal", "Cada golpe da Ceguera (hasta 4 supervivientes): no ves auras.", "Each hit gives Blindness (up to 4 Survivors): no Auras." },
                new[] { "Thrill of the Hunt", "Al colgar, bloquea sus tótems de maleficio 6 s por cada uno encendido.", "On hook, blocks its Hex Totems 6 s for each one lit." },
                new[] { "Blood Favor", "Al herir a alguien, los palés a 32 m quedan bloqueados (15 s).", "Injuring someone blocks pallets within 32 m (15 s)." },
                new[] { "Retribution", "Empezar a limpiar un tótem: Inconsciencia (60 s). Limpiar un maleficio: ve a todos (20 s).", "Starting to cleanse: Oblivious (60 s). Cleansing a hex: sees everyone (20 s)." },
                new[] { "Undying", "Si limpiás otro maleficio, pasa a este tótem. Ve a los que estén cerca de tótems.", "Cleansing another hex moves it here. Sees those near totems." },
                new[] { "Im All Ears", "Saltos rápidos o entrar/salir de taquilla a 48 m te revelan (8 s).", "Fast vaults or locker entries within 48 m reveal you (8 s)." },
                new[] { "Infectious Fright", "Al derribar a alguien, los que estén en su radio de terror gritan y los ubica.", "On a down, those in its Terror Radius scream and are located." },
                new[] { "Insidious", "Quieto 2 s: Indetectable. Típico para campear el gancho.", "Still for 2 s: Undetectable. Typical for camping the hook." },
                new[] { "Knock Out", "Tirás un palé y te alejás 10 m: Entorpecimiento 20 % (4 s).", "Drop a pallet and move 10 m away: 20% Hindered (4 s)." },
                new[] { "Iron Grasp", "Mientras te carga, forcejeás 12 % más lento y con 75 % menos fuerza.", "While carried, you wiggle 12% slower and 75% weaker." },
                new[] { "Iron Maiden", "Salir de una taquilla: gritás y Vulnerabilidad (30 s). Abre taquillas más rápido.", "Leaving a locker: you scream and get Exposed (30 s). Opens lockers faster." },
                new[] { "Hoarder", "+2 cofres. Le avisa si agarrás un objeto o abrís un cofre a 64 m.", "+2 chests. Alerted when you pick up an item or open a chest within 64 m." },
                new[] { "Oppression", "Al patear un gen, hasta 4 gens más retroceden y dan pruebas difíciles.", "Kicking a gen makes up to 4 more regress, with hard skill checks." },
                new[] { "Coup de Grace", "Cada gen terminado da 2 fichas: estocadas 80 % más largas.", "Each gen done gives 2 tokens: 80% longer lunges." },
                new[] { "Starstruck", "Mientras carga a alguien, los que estén en su radio: Vulnerabilidad (30 s después).", "While carrying, those in its Terror Radius: Exposed (lasts 30 s after)." },
                new[] { "Crowd Control", "Las últimas 6 ventanas que saltás rápido quedan bloqueadas.", "The last 6 windows you fast-vault get blocked." },
                new[] { "No Way Out", "En el End Game, al abrir una puerta se bloquea 12 s + 12 s por cada colgado.", "In the Endgame, opening a gate blocks it 12 s + 12 s per Survivor hooked." },
                new[] { "Lethal Pursuer", "Al empezar ve a todos (9 s). Sus auras le duran 2 s más.", "At start sees everyone (9 s). Its Aura reveals last 2 s longer." },
                new[] { "Hysteria", "Al herir a alguien sano, todos los heridos: Inconsciencia (40 s).", "When a healthy Survivor is injured, all injured get Oblivious (40 s)." },
                new[] { "Eruption", "Gens pateados: al derribar, explotan (−10 %), retroceden y los que reparan gritan.", "Kicked gens: on a down they explode (−10%), regress and repairers scream." },
                new[] { "Deadlock", "Gen terminado: el gen más avanzado queda bloqueado (25 s).", "Gen done: the most advanced gen is blocked (25 s)." },
                new[] { "Plaything", "Al colgarte: Inconsciencia hasta limpiar su tótem (los otros no lo pueden limpiar 90 s).", "On your hook: Oblivious until you cleanse its totem (others can't for 90 s)." },
                new[] { "Gift of Pain", "Desenganche de castigo: Hemorragia y Laceración; ya curado, reparás 16 % más lento.", "Scourge Hook unhook: Hemorrhage and Mangled; once healed, 16% slower repairs." },
                new[] { "Grim Embrace", "Cada primer gancho bloquea todos los gens 10 s; con 4, 40 s y ve a la obsesión.", "Each first hook blocks all gens 10 s; at 4, 40 s and it sees the Obsession." },
                new[] { "Pain Resonance", "4 ganchos de castigo: al colgar ahí, el gen más avanzado explota (−20 %) (4 veces).", "4 Scourge Hooks: hooking there explodes the most advanced gen (−20%) (4 times)." },
                new[] { "Pentimento", "Reaviva tótems limpiados: con uno, curás y reparás 20 % más lento.", "Rekindles cleansed totems: with one, you heal and repair 20% slower." },
                new[] { "Floods of Rage", "4 ganchos de castigo: al desenganchar de uno, ve a todos los demás (7 s).", "4 Scourge Hooks: an unhook from one shows it everyone else (7 s)." },
                new[] { "Call of Brine", "Gen pateado: retrocede 50 % más rápido (90 s) y le avisa cada prueba buena.", "Kicked gen: regresses 50% faster (90 s) and good checks alert it." },
                new[] { "Merciless Storm", "Gen al 90 %: pruebas difíciles seguidas; si fallás o soltás, se bloquea (20 s).", "Gen at 90%: chain of hard checks; miss or stop and it's blocked (20 s)." },
                new[] { "Dissolution", "Tras golpear a alguien, el próximo palé que saltes en su radio se rompe.", "After a hit, the next pallet you vault in its Terror Radius breaks." },
                new[] { "Darkness Revealed", "Al revisar una taquilla, ve a los que estén a 8 m de taquillas (8 s).", "Searching a locker shows it those within 8 m of lockers (8 s)." },
                new[] { "Septic Touch", "Curarte en su radio de terror: Ceguera y Agotamiento.", "Healing in its Terror Radius: Blindness and Exhausted." },
                new[] { "Superior Anatomy", "Si saltás cerca (12 m), su próximo salto de ventana es 40 % más rápido.", "If you vault nearby (12 m), its next window vault is 40% faster." },
                new[] { "Awakened Awareness", "Mientras carga a alguien, ve a los que estén a 20 m.", "While carrying, sees those within 20 m." },
                new[] { "Terminus", "En el End Game, heridos, derribados y colgados: Desesperanza.", "Endgame: injured, downed and hooked get Broken." },
                new[] { "Nowhere to Hide", "Al patear un gen, ve a los que estén a 24 m de él (5 s).", "Kicking a gen shows it those within 24 m (5 s)." },
                new[] { "Face the Darkness", "Mientras el herido siga herido, cada 25 s los que estén lejos gritan y los ve.", "While the hexed one stays injured, every 25 s far Survivors scream and are seen." },
                new[] { "Hubris", "Si lo aturdís: Vulnerabilidad (30 s).", "If you stun it: Exposed (30 s)." },
                new[] { "Game Afoot", "Persiguiendo a la obsesión, romper o patear da 10 % Celeridad (10 s).", "Chasing the Obsession, breaking or kicking gives 10% Haste (10 s)." },
                new[] { "THWACK", "Al romper un palé o pared, los que estén a 36 m gritan y los ve (6 s).", "Breaking a pallet or wall: those within 36 m scream and are seen (6 s)." },
                new[] { "Leverage", "Desenganchar (a otro o a vos): curás 30 % más lento (60 s).", "Unhooking (anyone or yourself): you heal 30% slower (60 s)." },
                new[] { "Genetic Limits", "Cada golpe: Agotamiento (8 s).", "Every hit: Exhausted (8 s)." },
                new[] { "Forced Hesitation", "Al derribar, los que estén a 16 m: Entorpecimiento 20 % (10 s).", "On a down, those within 16 m: 20% Hindered (10 s)." },
                new[] { "Machine Learning", "Si terminan un gen que pateó: 8 % Celeridad e Indetectable (60 s).", "If you finish a gen it kicked: 8% Haste and Undetectable (60 s)." },
                new[] { "Ultimate Weapon", "Al abrir una taquilla, los que estén a 40 m gritan y quedan con Ceguera (30 s).", "Opening a locker: those within 40 m scream and get Blindness (30 s)." },
                new[] { "Rapid Brutality", "Cada golpe da 5 % Celeridad (10 s). No consigue sed de sangre.", "Each hit gives 5% Haste (10 s). No Bloodlust." },
                new[] { "Alien Instinct", "Al colgar, el herido más lejano: Inconsciencia (60 s) y lo ve (8 s).", "On hook, the farthest injured: Oblivious (60 s) and seen (8 s)." },
                new[] { "Two Can Play", "Cegarlo o aturdirlo te ciega 1,5 s.", "Blinding or stunning it blinds you for 1.5 s." },
                new[] { "Friends Til the End", "Colgar a otro: la obsesión, Vulnerabilidad (20 s) y la ve.", "Hooking another: the Obsession gets Exposed (20 s) and is seen." },
                new[] { "Batteries Included", "A 16 m de un gen terminado: 5 % Celeridad.", "Within 16 m of a completed gen: 5% Haste." },
                new[] { "Unbound", "Tras herir a alguien, saltar ventanas le da 5 % Celeridad (25 s).", "After injuring someone, vaulting gives it 5% Haste (25 s)." },
                new[] { "Unforeseen", "Gen pateado: el radio de terror sale de ese gen y él queda Indetectable (30 s).", "Kicked gen: the Terror Radius comes from that gen, it's Undetectable (30 s)." },
                new[] { "Undone", "Cada gancho da fichas: su próxima patada saca 10 % más y bloquea 10 s por ficha.", "Hooks give tokens: its next kick takes 10% more and blocks 10 s per token." },
                new[] { "Weave Attunement", "Los objetos gastados se caen; cerca de uno tirado, te ve. Agarrarlos: Inconsciencia.", "Depleted items drop; near one, it sees you. Picking up: Oblivious." },
                new[] { "Languid Touch", "Espantar un cuervo a 36 m: Agotamiento (10 s).", "Scaring a crow within 36 m: Exhausted (10 s)." },
                new[] { "Dark Arrogance", "Salta 25 % más rápido y falla menos, pero cegadas y palés le duran 25 % más.", "Vaults 25% faster and recovers faster, but blinds and pallets last 25% longer." },
                new[] { "Wretched Fate", "Desde el primer gen: la obsesión repara 33 % más lento.", "From the first gen: the Obsession repairs 33% slower." },
                new[] { "Human Greed", "Cierra cofres; acercarte a uno cerrado te revela (5 s).", "Locks chests; getting near a locked one reveals you (5 s)." },
                new[] { "Dominance", "La primera vez que tocan cada tótem: se bloquea (25 s), gritás y te ve.", "First touch on each totem: blocked (25 s), you scream and are seen." },
                new[] { "All Shaking Thunder", "Al caer de una altura, estocadas 75 % más largas (25 s).", "After dropping from a height, 75% longer lunges (25 s)." },
                new[] { "Jagged Compass", "Los ganchos donde te desenganchan pasan a ser de castigo; ve el gen más avanzado.", "Unhooked hooks become Scourge Hooks; it sees the most advanced gen." },
                new[] { "No Quarter", "Curándote solo al 75 %: pruebas difíciles; si fallás, Desesperanza (30 s).", "Self-healing at 75%: hard checks; miss and get Broken (30 s)." },
                new[] { "Nothing But Misery", "Cada golpe: 5 % Entorpecimiento y saltos 10 % más lentos (15 s).", "Each hit: 5% Hindered and 10% slower vaults (15 s)." },
                new[] { "Forever Entwined", "Cada golpe le da fichas: cuelga y levanta más rápido (hasta 32 %).", "Each hit gives tokens: faster hooking and pick-ups (up to 32%)." },
                new[] { "None Are Free", "En el End Game, palés y ventanas bloqueados 16 s por cada colgado.", "Endgame: pallets and windows blocked 16 s per Survivor hooked." },
                new[] { "Help Wanted", "Si terminan un gen que pateó: los demás retroceden al 150 % y golpea más rápido (90 s).", "If you finish a gen it kicked: others regress at 150%, faster hits (90 s)." },
                new[] { "Phantom Fear", "Si lo mirás estando en su radio, gritás y te ve (2 s).", "Looking at it in its Terror Radius makes you scream; seen for 2 s." },
                new[] { "Haywire", "Las puertas abiertas al 80 % o más retroceden; las luces parpadean.", "Gates at 80% or more regress; their lights flicker." },
                new[] { "Ravenous", "Al colgar a los 4: todos gritan y quedan con Vulnerabilidad (90 s).", "After hooking all 4: everyone screams and gets Exposed (90 s)." },
                new[] { "Wandering Eye", "Al empezar una persecución, ve a los heridos a 20 m (5 s).", "Starting a chase shows it injured Survivors within 20 m (5 s)." },
                new[] { "Overture of Doom", "Reparar el gen marcado 5 s: Indetectable y el radio de terror sale de ese gen.", "Repairing the marked gen 5 s: Undetectable, Terror Radius from that gen." },
                new[] { "Turn Back The Clock", "Tras colgar, puede hacer explotar un gen que vea a 20 m (−10 %).", "After a hook, can explode a gen it sees within 20 m (−10%)." },
                new[] { "Secret Project", "Bendecir o limpiar un tótem bloquea un gen al azar (30 s).", "Blessing or cleansing a totem blocks a random gen (30 s)." },
                new[] { "Hive Mind", "Ve el progreso de los gens. Con 1 gen restante, todos explotan (−10 %).", "Sees gen progress. With 1 gen left, all explode (−10%)." },
                new[] { "Scared to Death", "Romper un palé en persecución: los que estén a 13 m gritan y Entorpecimiento.", "Breaking a pallet in chase: those within 13 m scream and get Hindered." },
                new[] { "Silent Shadow", "Al colgar: Indetectable (13 s). En el End Game: siempre.", "On hook: Undetectable (13 s). Endgame: always." },
                new[] { "Rampage", "Cada palé roto le da fichas: al cegarlo o aturdirlo gana Celeridad.", "Each broken pallet gives tokens: blinding or stunning it gives Haste." },
                new[] { "Celestial Witness", "Cada 30 s ve a la obsesión si está a más de 40 m.", "Every 30 s sees the Obsession if farther than 40 m." },
                new[] { "Under Your Thumb", "Tu Celeridad corriendo no pasa del 15 %; le avisa cuando la usás.", "Your running Haste caps at 15%; it's alerted when you use it." },
                new[] { "Lay Waste", "Al patear un gen, retrocede más rápido según sus cargas.", "Kicking a gen makes it regress faster based on its charges." },
                new[] { "Lightborn", "No lo podés cegar, y al intentarlo te ve (10 s).", "Can't be blinded; trying it reveals you (10 s)." },
                new[] { "Mad Grit", "Mientras te carga, si falla un golpe se recupera al instante; si pega, no avanzás el forcejeo.", "While carrying, missed swings have no cooldown; hits pause your wiggle." },
                new[] { "Make your Choice", "Desenganchar con él a 32 m o más: gritás y Vulnerabilidad (60 s).", "Unhooking while it's 32 m+ away: you scream and get Exposed (60 s)." },
                new[] { "Mindbreaker", "Mientras reparás: Ceguera y Agotamiento (hasta 5 s después).", "While repairing: Blindness and Exhausted (until 5 s after)." },
                new[] { "Monitor & Abuse", "Fuera de persecución, radio de terror 25 % más chico; persiguiendo, 15 % más grande.", "Out of chase, 25% smaller Terror Radius; in chase, 15% bigger." },
                new[] { "Monstrous Shrine", "Sótano de castigo: con alguien colgado ahí y él lejos, los gens retroceden al 200 %.", "Scourge basement: with someone hooked there and it away, gens regress at 200%." },
                new[] { "Nemesis", "Cegarlo o aturdirlo te hace la obsesión: Inconsciencia (60 s) y te ve (8 s).", "Blinding or stunning it makes you Obsession: Oblivious (60 s), seen (8 s)." },
                new[] { "No One Escapes Death", "En el End Game: Vulnerabilidad para todos hasta limpiar su tótem.", "Endgame: everyone Exposed until its totem is cleansed." },
                new[] { "A Nurses Calling", "Ve a los que se curan o están siendo curados a 32 m.", "Sees those healing or being healed within 32 m." },
                new[] { "Overwhelming Presence", "Usar un objeto a 32 m: Agotamiento (15 s).", "Using an item within 32 m: Exhausted (15 s)." },
                new[] { "Play With Your Food", "Cada vez que deja de perseguir a la obsesión: 5 % Celeridad (hasta 3).", "Each time it stops chasing the Obsession: 5% Haste (up to 3)." },
                new[] { "Pop Goes the Weasel", "Tras colgar (45 s), su próxima patada al gen saca 15 % más.", "After a hook (45 s), its next gen kick takes 15% more." },
                new[] { "Predator", "Cuando te pierde en persecución, te ve (4 s).", "When it loses you in chase, it sees you (4 s)." },
                new[] { "Rancor", "En el End Game: la obsesión, Vulnerabilidad y puede hacerle mori.", "Endgame: the Obsession is Exposed and can be killed." },
                new[] { "Remember Me", "Cada golpe a la obsesión hace que los demás abran puertas 30 % más lento.", "Each hit on the Obsession: others open gates 30% slower." },
                new[] { "Save the best for last", "Golpear a otros (no a la obsesión) le acorta la pausa tras golpear.", "Hitting others (not the Obsession) shortens its hit cooldown." },
                new[] { "Shadowborn", "Cegarlo le da 10 % Celeridad (10 s).", "Blinding it gives it 10% Haste (10 s)." },
                new[] { "Sloppy Butcher", "Cada golpe: Hemorragia y Laceración; perdés progreso de curación.", "Each hit: Hemorrhage and Mangled; healing progress drains." },
                new[] { "Spies from the Shadows", "Espantar un cuervo a 40 m le avisa dónde estás.", "Scaring a crow within 40 m alerts it." },
                new[] { "Spirit Fury", "Después de romper 2 palés, el próximo que lo aturda se rompe.", "After breaking 2 pallets, the next one that stuns it breaks." },
                new[] { "Stridor", "Tu respiración se escucha 25 % más fuerte y los quejidos 50 % más.", "Your breathing is 25% louder and grunts 50% louder." },
                new[] { "Surge", "Al derribar, los gens a 32 m explotan (−8 %) y retroceden.", "On a down, gens within 32 m explode (−8%) and regress." },
                new[] { "Surveillance", "Ve los gens que retroceden y escucha las reparaciones desde más lejos.", "Sees regressing gens and hears repairs from farther away." },
                new[] { "Territorial Imperative", "Entrar al sótano con él lejos te revela (6 s).", "Entering the basement while it's away reveals you (6 s)." },
                new[] { "Thanatophobia", "Por cada herido, derribado o colgado, todos reparan 2 % más lento (14 % más con los 4).", "Each injured, downed or hooked: everyone repairs 2% slower (14% more with all 4)." },
                new[] { "Thrilling Tremors", "Al levantar a alguien, los gens que no se reparan quedan bloqueados (16 s).", "Picking someone up blocks gens not being repaired (16 s)." },
                new[] { "Tinkerer", "Gen al 70 %: le avisa y queda Indetectable (16 s).", "Gen at 70%: it's alerted and Undetectable (16 s)." },
                new[] { "Trail of Torment", "Al patear un gen, queda Indetectable mientras ese gen retrocede.", "Kicking a gen makes it Undetectable while that gen regresses." },
                new[] { "Unnerving Presence", "En su radio de terror: más pruebas y 60 % más chicas.", "In its Terror Radius: more skill checks, 60% smaller." },
                new[] { "Unrelenting", "Se recupera 40 % más rápido si falla un golpe y 10 % si pega.", "Recovers 40% faster from missed swings and 10% from hits." },
                new[] { "Whispers", "Escucha susurros si hay alguien a 24 m; si no, 5 % Celeridad.", "Hears whispers when someone is within 24 m; otherwise 5% Haste." },
                new[] { "Zanshin Tactics", "Ve palés y ventanas a 32 m; tirar un palé te revela (5 s).", "Sees pallets and windows within 32 m; dropping a pallet reveals you (5 s)." },
            };
            static Dictionary<string, string[]> perkBriefs;
            static string PerkBrief(Entry e)
            {
                if (perkBriefs == null)
                {
                    perkBriefs = new Dictionary<string, string[]>();
                    foreach (var p in PerkBriefList) perkBriefs[Bare(p[0])] = p;
                }
                string[] b;
                string k = Bare(Regex.Replace(e.Name, @"^(Hex|Scourge Hook|Boon|Invocation):\s*", ""));
                return perkBriefs.TryGetValue(k, out b) ? (DescEsNow ? b[1] : b[2]) : AddonBrief(e); // uno nuevo: resumen automatico
            }

            static string BriefOf(Entry e)
            {
                string[] b;
                if (e != null && e.K == Kind.Addon) return AddonBrief(e);
                if (e != null && e.K == Kind.Perk) return PerkBrief(e);
                if (e == null || e.K != Kind.Offering || e.Action != null) return null; // los mapas tambien son "Offering" (con Action): sin descripcion
                if (Brief.TryGetValue(e.Name, out b)) return L(b[0], b[1]);
                // una que no esta en la lista (nueva): la descripcion del juego sin el "Secreto." del final
                string d = Regex.Replace((DescEsNow ? e.Info : e.InfoEn) ?? "", @"\s*(Secreto|Secret)\.?\s*$", "").Trim();
                return d.Length > 0 ? d : null;
            }

            // addon: el efecto en una o dos oraciones (sin citas, ambientacion ni la explicacion general de un estado)
            static readonly Dictionary<string, string> briefCache = new Dictionary<string, string>();
            static readonly Regex EffectStart = new Regex(@"^(Aumenta|Reduce|Disminuye|Los supervivientes|Al |Cuando|Mientras|Tras|Si |Cada|Obtienes|Revela|Otorga|Permite|Hace|Usa|Golpear|Consume|Todos|Puedes|Durante|Increases|Decreases|Reduces|Survivors|When|While|After|Upon|If |Each|Every|Gain|Reveals|Grants|Allows|Causes|Using|Direct|Automatically|All |Your|You |Hatchets|Setting|Sets|Rush|Lowers|Slightly|Moderately|Considerably|Tremendously)", RegexOptions.IgnoreCase);
            static readonly Regex GlossaryLike = new Regex(@"^\p{Lu}[\p{L}-]+(\s[\p{L}-]+)?\s(impide|oculta|hace que|provoca|reduce la velocidad|prevents|hides|causes|makes|will put|reduces Survivor)\b");
            static readonly Regex GlossaryTail = new Regex(@"(?<=\d\s?(?:s|segundos|seconds))\s+\p{Lu}[\p{L}-]+\s(impide|oculta|prevents|hides)\b.*$"); // sin punto en el original: "…6 s Agotamiento impide…"
            // "Aumenta la duracion de X un 20 %" -> "+20 % duracion de X"; "Decreases X by 0.5 seconds" -> "-0.5 s X"
            static string Shorten(string s)
            {
                s = s.Trim().TrimEnd('.');
                s = Regex.Replace(s, @":(?=\S)", ": ");
                string num = @"(\d[\d.,]*(?:\s?(?:%|m/s|metros?|meters?|segundos?|seconds?|grados|degrees|m|s)(?!\p{L}))?)";
                var m = Regex.Match(s, @"^(Aumenta|Reduce|Disminuye)\s+(?:en\s+)?(?:un\s+)?(.+?)\s+(?:en\s+|un\s+)+" + num + @"(.*)$");
                if (!m.Success) m = Regex.Match(s, @"^(Increases|Decreases|Reduces|Lowers)\s+(.+?)\s+by\s+" + num + @"(.*)$");
                if (m.Success)
                {
                    string sign = Regex.IsMatch(m.Groups[1].Value, "^(Aumenta|Increases)") ? "+" : "−";
                    string what = Regex.Replace(m.Groups[2].Value, @"^(el|la|los|las|the)\s+", "", RegexOptions.IgnoreCase);
                    what = Regex.Replace(what, @"^(tiempo que tardan? en|tiempo necesario para|time required to)\s+", "", RegexOptions.IgnoreCase);
                    s = sign + m.Groups[3].Value.Trim() + " " + what + m.Groups[4].Value;
                    s = Regex.Replace(s, @"^([+−]\S+(?:\s(?:%|m|s|segundos?|seconds?|metros?|meters?))?)\s+en\s+(?:la|el|los|las)\s+", "$1 "); // "+1 s en la duracion" -> "+1 s duracion"
                }
                s = Regex.Replace(s, @"\s+(durante|for)\s+(\d[\d.,]*\s?(?:segundos|seconds|s))\b", " ($2)");
                return s.Trim() + ".";
            }

            // estados del juego: en la descripcion van como su icono (el que el juego muestra en pantalla)
            static readonly string[][] StatusIcons = {
                new[] { "exhausted", "Agotamiento", "Exhausted" }, new[] { "hindered", "Entorpecimiento", "Hindered" },
                new[] { "undetectable", "Indetectable", "Undetectable" }, new[] { "exposed", "Vulnerabilidad", "Exposed" },
                new[] { "deepWound", "Herida profunda", "Deep Wound" }, new[] { "oblivious", "Inconsciencia", "Oblivious" },
                new[] { "incapacitated", "Incapacitación", "Incapacitated" }, new[] { "bleeding", "Hemorragia", "Hemorrhage" },
                new[] { "bleeding", "Hemorragia", "Haemorrhage" }, new[] { "mangled", "Laceración", "Mangled" },
                new[] { "broken", "Desesperanza", "Broken" }, new[] { "haste", "Celeridad", "Haste" }, new[] { "madness", "Locura", "Madness" }, new[] { "blindness", "Ceguera", "Blindness" } };
            public static bool DescEsNow = true;   // idioma de las descripciones (Configuracion > General)
            public static string StatusMode = "ambos"; // ambos | icono | texto
            static string Iconize(string s)
            {
                if (StatusMode == "texto")
                {
                    // sin iconos: igual se saca el relleno ("el efecto de estado X" -> "X")
                    s = Regex.Replace(s, @"(?:el\s+)?efecto\s+de\s+estado\s+", "");
                    return Regex.Replace(s, @"(?:the\s+)?(\p{Lu}[\p{L} ]*?)\s+status\s+effect\b", "$1");
                }
                foreach (var si in StatusIcons)
                foreach (var w in new[] { si[1], si[2] })
                {
                    string ph = "\u0001" + si[0] + "\u0001";
                    // "el efecto de estado Agotamiento" / "the Exhausted status effect" -> el icono solo
                    s = Regex.Replace(s, @"(?:el\s+)?efecto\s+de\s+estado\s+" + Regex.Escape(w) + @"\b", ph);
                    s = Regex.Replace(s, @"(?:the\s+)?" + Regex.Escape(w) + @"\s+status\s+effect\b", ph);
                    s = Regex.Replace(s, @"(?<![\p{L}])" + Regex.Escape(w) + @"(?![\p{L}])", ph);
                }
                return s;
            }
            static readonly Regex StatusTok = new Regex(@"^(\(?)\u0001(\w+)\u0001(.*)$");
            static string StatusWord(string key)
            {
                var si = StatusIcons.FirstOrDefault(x => x[0] == key);
                return si == null ? key : DescEsNow ? si[1] : si[2];
            }

            static string AddonBrief(Entry e)
            {
                string key = (DescEsNow ? "es|" : "en|") + e.Killer + "|" + e.Name, b;
                if (briefCache.TryGetValue(key, out b)) return b;
                string all = ((DescEsNow ? e.Info : e.InfoEn) ?? "").Replace("\r", "");
                var paras = Regex.Split(all, @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0 && !Regex.IsMatch(p, "^[\"“”«]")).ToList();
                string para = paras.FirstOrDefault(p => EffectStart.IsMatch(p)) ?? paras.FirstOrDefault() ?? "";
                para = Regex.Replace(para, @"\s+", " ");
                var sents = Regex.Split(para, @"(?<=[.!?])\s+(?=\p{Lu})").Where(s => s.Length > 0).ToList();
                // "Agotamiento impide que…" / "Exhausted prevents…": explica el estado en general, no el addon
                sents = sents.Where((s, i) => i == 0 || !GlossaryLike.IsMatch(s)).ToList();
                if (sents.Count > 0) sents[0] = GlossaryTail.Replace(sents[0], ".");
                // una oracion (dos solo si la primera es muy corta), en forma corta: "+20 % velocidad…", "(10 s)"
                int take = sents.Count > 1 && sents[0].Length < 45 ? 2 : 1;
                string res = string.Join(" ", sents.Take(take).Select(Shorten));
                if (sents.Count > take) res += " …";
                res = Regex.Replace(res, @"(\d)\s*(metros|meters|metres)\b", "$1 m");
                res = Regex.Replace(res, @"(\d)\s*(segundos|seconds)\b", "$1 s");
                res = Regex.Replace(res, @"(\d)\s*(segundo|second)\b", "$1 s");
                res = res.Trim();
                briefCache[key] = res.Length > 0 ? res : null;
                return briefCache[key];
            }

            static int OffGroup(Entry e)
            {
                if (e.K != Kind.Offering || e.Action != null) return -1;
                string n = e.Name;
                if (e.Rarity == "mapa") return 0;
                if (n.Contains("Memento Mori")) return 1;
                if (e.Rarity == "ganchos" || n.EndsWith(" Oak")) return 2;     // ganchos (distancia entre ganchos)
                if (e.Rarity == "cofres" || n.EndsWith(" Coin")) return 3;     // cofres
                if (e.Rarity == "niebla" || n.EndsWith(" Reagent")) return 4;  // niebla
                if (n.Contains("Bloodied Blueprint") || n.Contains("Torn Blueprint")) return 5; // sotano
                if (n.Contains("Blueprint")) return 6;      // trampilla
                return 7;                                   // Black Ward, Shroud of Vanishing, otras
            }
            static string LetterOf(string n)
            {
                string s = Txt.Norm(n);
                return s.Length > 0 && char.IsLetter(s[0]) ? s.Substring(0, 1).ToUpperInvariant() : "#";
            }

            // icono centrado en (cx, cy): los cuadrados ocupan s x s; los anchos (fotos de mapas) un poco mas de ancho, sin deformarse
            // abre otra rueda (categoria, letra, reino, "mas…", quitar); deshacer y lo que se elige, no
            static bool IsSub(Seg s) { return s.E == null && s.Glyph != "↶" && !s.Leaf; }
            static bool IsPhoto(Seg s)
            {
                if (s.E != null) return s.E.Icon != null && s.E.Action != null && s.E.Action.StartsWith("mapa:");
                return s.Icon != null && s.Icon.StartsWith("IconMap_");
            }
            static void Fit(Graphics g, Image im, float cx, float cy, float s)
            {
                float sc = Math.Min(s * 1.4f / im.Width, s / (float)im.Height);
                int w = Math.Max(1, (int)Math.Round(im.Width * sc)), h = Math.Max(1, (int)Math.Round(im.Height * sc));
                g.DrawImageUnscaled(Scaled(im, w, h), (int)Math.Round(cx - w / 2f), (int)Math.Round(cy - h / 2f));
            }
            // iconos ya achicados (achicar en alta calidad en cada repintada era lo que hacia lenta la rueda)
            static readonly Dictionary<Tuple<Image, int, int>, Bitmap> scaledCache = new Dictionary<Tuple<Image, int, int>, Bitmap>();
            static Bitmap Scaled(Image im, int w, int h)
            {
                var k = Tuple.Create(im, w, h);
                Bitmap b;
                if (scaledCache.TryGetValue(k, out b)) return b;
                b = new Bitmap(w, h);
                using (var g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(im, 0, 0, w, h);
                }
                return scaledCache[k] = b;
            }
            // la foto de un mapa recortada con la forma de su porcion (por cantidad de porciones y posicion)
            static readonly Dictionary<Tuple<Image, int, int>, Bitmap> photoCache = new Dictionary<Tuple<Image, int, int>, Bitmap>();
            static Bitmap SegPhoto(Image photo, System.Drawing.Drawing2D.GraphicsPath path, int n, int i)
            {
                var k = Tuple.Create(photo, n, i);
                Bitmap b;
                if (photoCache.TryGetValue(k, out b)) return b;
                var bb = path.GetBounds();
                int x0 = (int)Math.Floor(bb.X), y0 = (int)Math.Floor(bb.Y);
                b = new Bitmap(Math.Max(1, (int)Math.Ceiling(bb.Right) - x0), Math.Max(1, (int)Math.Ceiling(bb.Bottom) - y0));
                using (var g = Graphics.FromImage(b))
                using (var p = (System.Drawing.Drawing2D.GraphicsPath)path.Clone())
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    using (var mx = new System.Drawing.Drawing2D.Matrix()) { mx.Translate(-x0, -y0); p.Transform(mx); }
                    g.SetClip(p);
                    float sc = Math.Max(bb.Width / photo.Width, bb.Height / photo.Height);
                    float pw = photo.Width * sc, ph = photo.Height * sc;
                    g.DrawImage(photo, bb.X - x0 + (bb.Width - pw) / 2, bb.Y - y0 + (bb.Height - ph) / 2, pw, ph);
                }
                return photoCache[k] = b;
            }

            Level Root()
            {
                var lv = new Level { Title = L("¿QUÉ VISTE?", "WHAT DID YOU SEE?") };
                // iconos genericos del juego (los de la ayuda de la wiki)
                Func<string, string, Func<List<Entry>>, Seg> C = (t, icon, items) => new Seg { Label = t, Big = t.ToUpperInvariant(), Icon = icon, Go = () => Push(Group(t, items())) };
                // perks: frecuentes + categorias (por defecto) o por letra (Configuracion > Rueda)
                lv.Segs.Add(new Seg { Label = "Perk", Big = "PERK", Icon = "IconHelp_perks.png",
                                      Go = () => Push(ov.cfg.Get("RuedaPerks", "categorias") == "letras" ? Group("Perk", ov.cat.Perks.ToList()) : PerkLevel()) });
                // addons: solo con el killer anotado (sin killer serian cientos y no sabrias cuales son de quien)
                if (ov.st.Killer != null)
                    lv.Segs.Add(C(L("Addon", "Add-on"), "IconHelp_addons.png", () => ov.cat.Addons.Where(a => a.Killer == ov.st.Killer.Name).ToList()));
                // ofrendas: solo las secretas (las demas el juego las muestra a todos al empezar)
                lv.Segs.Add(C(L("Ofrenda", "Offering"), "IconHelp_offerings.png", () => ov.cat.Offerings.Where(o => o.Synth || (o.Secret && !Regex.IsMatch(o.Name, @" (Reagent|Oak|Coin)$"))).ToList()));
                // killers: por tipo de poder (por defecto), originales/licenciados, mas jugados o por letra (Configuracion > Rueda)
                lv.Segs.Add(new Seg { Label = "Killer", Big = "KILLER", Icon = "IconHelpLoading_killer.png", Go = () => Push(KillerLevel()) });
                lv.Segs.Add(new Seg { Label = L("Mapa", "Map"), Big = L("MAPA", "MAP"), Icon = "IconItems_CrypticMap.png", Go = () =>
                {
                    var maps = ov.cat.Maps.Where(m => ov.cat.MapFiles.ContainsKey(m.Name)).ToList();
                    Push(ov.cfg.Get("RuedaMapas", "reino") == "letras" ? Group(L("Mapa", "Map"), maps) : RealmLevel(maps));
                } });
                // quitar: solo si hay algo anotado; muestra lo anotado y al elegir se borra
                if (ov.st.Current().Count > 0)
                    lv.Segs.Add(new Seg { Label = L("Quitar", "Remove"), Big = L("QUITAR", "REMOVE"), Glyph = "✖", Go = () => Push(RemoveLevel()) });
                // partida nueva (lo mismo que mantener la tecla de borrar): para quien no usa el teclado
                if (ov.HasAnything())
                    lv.Segs.Add(new Seg { Label = L("Partida nueva: borrar todo lo anotado", "New match: clear everything"), Big = L("PARTIDA NUEVA", "NEW MATCH"), Glyph = "↺", Leaf = true,
                                          Go = () => { Hide(); ov.ClearWithUndo(); } });
                if (ov.CanNextBuild)
                    lv.Segs.Add(ov.NextHidesBuild
                        ? new Seg { Label = L("Quitar la build sugerida", "Hide the suggested build"), Big = L("QUITAR BUILD", "HIDE BUILD"), Glyph = "✖", Leaf = true, Go = () => { Hide(); ov.NextBuild(); } }
                        : new Seg { Label = L("Otra build sugerida", "Next suggested build"), Big = L("OTRA BUILD", "NEXT BUILD"), Glyph = "⇄", Leaf = true,
                                          Go = () => { Hide(); ov.NextBuild(); } });
                if (ov.CanUndoClear)
                    lv.Segs.Add(new Seg { Label = L("Deshacer el borrado", "Undo the clear"), Big = L("DESHACER", "UNDO"), Glyph = "↶",
                                          Go = () => { Hide(); ov.UndoClear(); } });
                else if (ov.st.CanUndo)
                    lv.Segs.Add(new Seg { Label = L("Deshacer lo último", "Undo the last change"), Big = L("DESHACER", "UNDO"), Glyph = "↶",
                                          Go = () => { Hide(); ov.BoxDo(L("deshacer", "undo"), () => { string w = ov.st.Apply("deshacer"); int i = w.IndexOf(" · "); return i > 0 ? w.Substring(0, i) : w; }); } });
                return lv;
            }

            // categorias de perks por lo que hacen (se clasifican solos por la descripcion en ingles, sin depender del idioma)
            static readonly string[][] PerkCats = {
                new[] { "Maleficios", "Hexes", "IconHelpLoading_totem.png", "MALEFICIOS", "HEXES" },
                new[] { "Ganchos", "Hooks", "IconHelp_scourgeHooks.png", "GANCHOS", "HOOKS" },
                new[] { "Generadores bloqueados", "Blocked generators", "IconHelpLoading_generators.png", "BLOQUEO", "BLOCKING" },
                new[] { "Generadores que retroceden", "Generator regression", "IconHelpLoading_generatorRegression.png", "REGRESIÓN", "REGRESSION" },
                new[] { "Auras", "Auras", "IconHelp_auras.png", "AURAS", "AURAS" },
                new[] { "Información (gritos, avisos)", "Info (screams, alerts)", "IconHelp_hearing.png", "INFO", "INFO" },
                new[] { "Persecución", "Chase", "IconHelp_chase.png", "PERSECUCIÓN", "CHASE" },
                new[] { "Estados", "Status effects", "IconHelp_statusEffects.png", "ESTADOS", "STATUS" },
                new[] { "End Game", "Endgame", "IconHelp_exitGates.png", "END GAME", "ENDGAME" },
                new[] { "Otros", "Other", "IconHelp_perks.png", "OTROS", "OTHER" } };
            static int PerkCat(Entry e)
            {
                string n = e.Name, d = e.InfoEn ?? "";
                const RegexOptions I = RegexOptions.IgnoreCase;
                if (n.StartsWith("Hex:")) return 0;
                if (n.StartsWith("Scourge Hook:")) return 1;
                if (Regex.IsMatch(d, @"exit gates?|end ?game|endgame collapse|last generator is completed|all generators (are|have been) (completed|repaired|powered)", I)) return 8;
                if (Regex.IsMatch(d, @"generator", I)) return Regex.IsMatch(d, @"\bblock|cannot be repaired|can.t be repaired", I) ? 2 : 3;
                if (Regex.IsMatch(d, @"\baura", I)) return 4;
                if (Regex.IsMatch(d, @"Killer Instinct|reveal|scream|notification|location|loud noise", I)) return 5;
                if (Regex.IsMatch(d, @"Exposed|Hindered|Deep Wound|Mangled|Broken|Haemorrhage|Hemorrhage|Oblivious|Undetectable|Exhausted|Blindness|Incapacitated|Madness", I)) return 7;
                if (Regex.IsMatch(d, @"chase|pallet|vault|window|Endurance|lunge|Haste|movement speed|Terror Radius|Bloodlust|Obsession|attack", I)) return 6;
                if (Regex.IsMatch(d, @"hook|basement|sacrific|dying state|carry|pick ?up|wiggle", I)) return 1;
                return 9;
            }
            Level PerkLevel()
            {
                var lv = new Level { Title = "PERK" };
                for (int i = 0; i < PerkCats.Length; i++)
                {
                    int ci = i;
                    string name = L(PerkCats[i][0], PerkCats[i][1]);
                    if (!ov.cat.Perks.Any(p => PerkCat(p) == ci)) continue;
                    // se arma al entrar: los mas usados arriba (los tuyos y los de la comunidad), despues el resto por nombre
                    lv.Segs.Add(new Seg { Label = name, Big = L(PerkCats[i][3], PerkCats[i][4]), Icon = PerkCats[i][2], Group = 1, Go = () =>
                    {
                        var all = ov.cat.Perks.Where(p => PerkCat(p) == ci).ToList();
                        var top = new HashSet<Entry>(all.Where(p => ov.PerkScore(p) > 0).OrderByDescending(p => ov.PerkScore(p)).Take(Math.Max(3, all.Count / 3)));
                        var list = all.Where(top.Contains).OrderByDescending(p => ov.PerkScore(p))
                                      .Concat(all.Where(p => !top.Contains(p)).OrderBy(p => SortName(p), StringComparer.OrdinalIgnoreCase)).ToList();
                        // categorias grandes: los que casi nadie usa van a una segunda rueda (salvo que vos los hayas anotado)
                        var rare = all.Count > 12 ? list.Where(p => IsRarePerk(p) && ov.PerkScore(p) < 1000).ToList() : new List<Entry>();
                        var lvl = Items(name, list.Except(rare).ToList(), 0, top);
                        if (rare.Count > 0)
                            lvl.Segs.Add(new Seg { Label = L("Poco usados (", "Rarely used (") + rare.Count + ")", Glyph = "⋯", Big = "+" + rare.Count,
                                                   Go = () => Push(Items(name + L(" · poco usados", " · rarely used"), rare, 0)) });
                        Push(lvl);
                    } });
                }
                return lv;
            }

            // killers por tipo de poder: lo que ves que hace en la partida. Icono: el retrato de uno que lo representa.
            static readonly string[][] KillerPower = {
                new[] { "A distancia", "Ranged", "The Huntress", "The Huntress", "The Clown", "The Deathslinger", "The Trickster", "The Artist", "The Unknown",
                        "The Houndmaster", "The Lich" },
                new[] { "Movilidad", "Mobility", "The Nurse", "The Hillbilly", "The Nurse", "The Blight", "The Spirit", "The Demogorgon", "The Oni", "The Dredge",
                        "The Ghoul", "The Krasue", "The Dark Lord", "The Twins", "The Singularity", "The Cannibal" },
                new[] { "Sigilo", "Stealth", "The Shape", "The Wraith", "The Shape", "The Ghost Face", "The Pig", "The Onryō", "The Good Guy",
                        "The Xenomorph", "The Animatronic" },
                new[] { "Trampas y zona", "Traps & zoning", "The Trapper", "The Trapper", "The Hag", "The Nightmare", "The Skull Merchant", "The Knight",
                        "The Executioner", "The Cenobite" },
                new[] { "Estados e infección", "Status & infection", "The Plague", "The Doctor", "The Plague", "The Legion", "The Mastermind", "The Nemesis" } };
            static readonly string[] LicensedKillers = { "The Shape", "The Nightmare", "The Pig", "The Ghost Face", "The Demogorgon", "The Executioner",
                "The Nemesis", "The Cenobite", "The Onryō", "The Mastermind", "The Xenomorph", "The Good Guy", "The Lich", "The Dark Lord",
                "The Animatronic", "The Cannibal", "The Ghoul", "The First", "The Slasher" };
            Seg KillerGroup(string name, string rep, List<Entry> list, bool byPop)
            {
                var r = ov.cat.Killers.FirstOrDefault(k => k.Name == rep);
                return new Seg { Label = name + " (" + list.Count + ")", Big = name.ToUpperInvariant(), Icon = r != null ? r.Icon : "IconHelpLoading_killer.png",
                                 Go = () => Push(Items(name, byPop ? list.OrderByDescending(k => ov.KillerScore(k)).ToList() : list.OrderBy(k => SortName(k), StringComparer.OrdinalIgnoreCase).ToList(), 0)) };
            }
            Level KillerLevel()
            {
                string mode = ov.cfg.Get("RuedaKillers", "poder");
                var all = ov.cat.Killers.ToList();
                if (mode == "letras") return Group("Killer", all);
                var lv = new Level { Title = "KILLER" };
                if (mode == "licencia")
                {
                    var lic = all.Where(k => LicensedKillers.Contains(k.Name)).ToList();
                    lv.Segs.Add(KillerGroup(L("Originales", "Originals"), "The Trapper", all.Except(lic).ToList(), true));
                    lv.Segs.Add(KillerGroup(L("Licenciados", "Licensed"), "The Shape", lic, true));
                    return lv;
                }
                if (mode == "jugados")
                {
                    // los mas jugados directo; el resto en una segunda rueda
                    var pop = all.OrderByDescending(k => ov.KillerScore(k)).ToList();
                    var lvl = Items("Killer", pop.Take(15).ToList(), 0);
                    var rest = pop.Skip(15).OrderBy(k => SortName(k), StringComparer.OrdinalIgnoreCase).ToList();
                    if (rest.Count > 0)
                        lvl.Segs.Add(new Seg { Label = L("Los demás (", "The rest (") + rest.Count + ")", Glyph = "⋯", Big = "+" + rest.Count,
                                               Go = () => Push(Items(L("Killer · los demás", "Killer · the rest"), rest, 0)) });
                    return lvl;
                }
                var used = new HashSet<Entry>();
                foreach (var g in KillerPower)
                {
                    var list = g.Skip(3).Select(n => all.FirstOrDefault(k => k.Name == n)).Where(k => k != null).ToList();
                    used.UnionWith(list);
                    if (list.Count > 0) lv.Segs.Add(KillerGroup(L(g[0], g[1]), g[2], list, true));
                }
                var other = all.Where(k => !used.Contains(k)).ToList(); // los nuevos caen aca hasta que se los ubique
                if (other.Count > 0)
                    lv.Segs.Add(new Seg { Label = L("Otros", "Other") + " (" + other.Count + ")", Big = L("OTROS", "OTHER"), Icon = "IconHelpLoading_killer.png",
                                          Go = () => Push(Items(L("Otros", "Other"), other.OrderByDescending(k => ov.KillerScore(k)).ToList(), 0)) });
                return lv;
            }

            // mapas por reino (como en el juego): cada reino con la foto de uno de sus mapas; si tiene uno solo, se elige directo
            Level RealmLevel(List<Entry> maps)
            {
                var lv = new Level { Title = L("REINO", "REALM") };
                var singles = new List<Entry>();
                foreach (var g in maps.GroupBy(m => m.ActionTag ?? "").OrderBy(g => Regex.Replace(g.Key, "^The ", ""), StringComparer.OrdinalIgnoreCase))
                {
                    var list = g.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    // los reinos de un solo mapa (casi todos de licencias) van juntos
                    if (list.Count == 1 || g.Key.Length == 0) { singles.AddRange(list); continue; }
                    string realm = g.Key;
                    var rep = list.FirstOrDefault(m => m.Icon != null);
                    lv.Segs.Add(new Seg { Label = realm + " (" + list.Count + ")", Big = realm, Icon = rep != null ? rep.Icon : "IconItems_CrypticMap.png",
                                          Go = () => Push(Items(realm, list, 0)) });
                }
                if (singles.Count > 0)
                {
                    string t = L("Un mapa", "Single map");
                    var sl = singles.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    lv.Segs.Add(new Seg { Label = t + " (" + sl.Count + ")", Big = t, Glyph = "⋯", Go = () => Push(Items(t, sl, 0)) });
                }
                return lv;
            }

            Level RemoveLevel()
            {
                var lv = new Level { Title = L("¿QUÉ QUITO?", "REMOVE WHAT?") };
                foreach (var e in ov.st.Current())
                {
                    var en = e;
                    lv.Segs.Add(new Seg { Label = L("Quitar ", "Remove ") + ov.DName(en), E = en, Go = () => { Hide(); ov.BoxRemove(en); } });
                }
                return lv;
            }

            // muchos: primero la letra; pocos (los addons del killer anotado): directo
            Level Group(string title, List<Entry> items)
            {
                items = items.OrderBy(e => e.Synth ? 0 : 1).ThenBy(e => SortName(e), StringComparer.OrdinalIgnoreCase).ToList(); // "Ofrenda de mapa" primero
                if (items.Count <= 24)
                {
                    if (items.All(e => e.K == Kind.Addon)) items = items.OrderBy(e => RarRank(e.Rarity)).ThenBy(e => SortName(e), StringComparer.OrdinalIgnoreCase).ToList();
                    // ofrendas: por tipo (mapa, moris, oaks, coins, niebla, sotano, trampilla...) y dentro de cada tipo de la mas debil a la mas fuerte
                    else if (items.All(e => e.K == Kind.Offering && e.Action == null)) items = items.OrderBy(e => OffGroup(e)).ThenByDescending(e => RarRank(e.Rarity)).ThenBy(e => SortName(e), StringComparer.OrdinalIgnoreCase).ToList();
                    return Items(title, items, 0);
                }
                var lv = new Level { Title = title.ToUpperInvariant() };
                foreach (var g in items.GroupBy(e => LetterOf(SortName(e))).OrderBy(g => g.Key == "#" ? "~" : g.Key))
                {
                    var list = g.ToList(); string letter = g.Key;
                    lv.Segs.Add(new Seg { Label = title + " · " + letter + " (" + list.Count + ")", Big = letter, Small = list.Count.ToString(), Go = () => Push(Items(title + " · " + letter, list, 0)) });
                }
                return lv;
            }

            // los de una letra (16 por rueda; si hay mas, la ultima seccion es "mas")
            // con los 4 perks (o 2 addons) ya anotados: en vez de rechazarlo, se elige cual sale
            void PickOrSwap(Entry en)
            {
                var st = ov.st;
                if ((en.K == Kind.Perk || en.K == Kind.Addon) && !st.Current().Contains(en))
                {
                    var list = en.K == Kind.Perk ? st.Perks : st.Addons;
                    if (list.Count >= (en.K == Kind.Perk ? 4 : 2)) { Push(SwapLevel(en, list.ToList())); return; }
                }
                Hide();
                ov.WheelPick(en);
            }
            Level SwapLevel(Entry neu, List<Entry> list)
            {
                var lv = new Level { Title = L("¿CUÁL SALE?", "WHICH ONE GOES?"), Swap = neu };
                foreach (var o in list)
                {
                    var old = o;
                    lv.Segs.Add(new Seg { Label = L("Sale ", "Out: ") + ov.DName(old) + L("\nentra ", "\nin: ") + ov.DName(neu), E = old, Marked = true,
                                          Go = () => { Hide(); ov.WheelSwap(old, neu); } });
                }
                return lv;
            }
            Level Items(string title, List<Entry> items, int page, HashSet<Entry> top = null)
            {
                int per = items.Count <= 26 ? 26 : 16; // hasta 24 (los addons de un killer, las ofrendas secretas) entran en una sola rueda
                var lv = new Level { Title = title.ToUpperInvariant() };
                bool more = items.Count > per;
                int take = more ? per - 1 : per, pages = more ? (items.Count + take - 1) / take : 1;
                foreach (var e in items.Skip(page * take).Take(take))
                {
                    var en = e;
                    bool on = en == ov.st.Killer || ov.st.Perks.Contains(en) || ov.st.Addons.Contains(en) || en == ov.st.Offering || (en.Action != null && en.Action == "mapa:" + ov.mapShown);
                    lv.Segs.Add(new Seg { Label = ov.DName(en), E = en, Marked = on, Group = top != null ? (top.Contains(en) ? 0 : 1) : OffGroup(en),
                                          Go = () => PickOrSwap(en) }); // elegido: se cierra (o, con la lista llena, se elige cual sale)
                }
                if (more)
                {
                    int next = (page + 1) % pages;
                    lv.Segs.Add(new Seg { Label = L("Más… (", "More… (") + (next + 1) + "/" + pages + ")", Big = "…", Go = () => { stack.RemoveAt(stack.Count - 1); Push(Items(title, items, next, top)); } });
                }
                return lv;
            }

            void Push(Level l) { stack.Add(l); hover = -1; Invalidate(); }
            void Back()
            {
                if (stack.Count > 1) stack.RemoveAt(stack.Count - 1); else { Hide(); return; }
                hover = -1; Invalidate();
            }
            Level Cur { get { return stack[stack.Count - 1]; } }

            int HitTest(Point p)
            {
                double dx = p.X - D / 2.0, dy = p.Y - D / 2.0, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < Rin) return -2;
                if (dist > R) return -1;
                int n = Cur.Segs.Count;
                if (n == 0) return -1;
                double per = 360.0 / n;
                double ang = Math.Atan2(dy, dx) * 180 / Math.PI + 90 + per / 2; // 0 = arriba, sentido horario
                ang = ((ang % 360) + 360) % 360;
                return Math.Min(n - 1, (int)(ang / per));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (Gesture) return;
                int h = HitTest(e.Location);
                if (h != hover) { hover = h; Invalidate(); }
            }
            protected override void OnMouseLeave(EventArgs e) { if (!Gesture && hover != -1) { hover = -1; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                if (Gesture) return;
                if (e.Button == MouseButtons.Right) { Back(); return; }
                if (e.Button != MouseButtons.Left) return;
                int h = HitTest(e.Location);
                if (h == -2) Back();
                else if (h >= 0) Cur.Segs[h].Go();
            }

            // la rueda se dibuja en una imagen y solo se rehace si cambio algo; mover el puntero (modo gesto) solo repinta el punto
            Bitmap cache; bool fullNeeded = true, pointerOnly;
            protected override void OnInvalidated(InvalidateEventArgs e)
            {
                if (!pointerOnly) fullNeeded = true;
                pointerOnly = false;
                base.OnInvalidated(e);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (cache == null || cache.Width != ClientSize.Width || cache.Height != ClientSize.Height)
                {
                    if (cache != null) cache.Dispose();
                    cache = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                    fullNeeded = true;
                }
                if (fullNeeded)
                {
                    using (var cg = Graphics.FromImage(cache)) PaintWheel(new PaintEventArgs(cg, new Rectangle(Point.Empty, cache.Size)));
                    fullNeeded = false;
                }
                e.Graphics.DrawImageUnscaled(cache, 0, 0);
                if (Gesture && steering && (vx != 0 || vy != 0) && ov.cfg.Get("RuedaPuntero", "no") == "si")
                {
                    // el "cursor" de la rueda: hacia donde estas apuntando
                    var g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    float c = D / 2f, k = (R - 24) / SteerMax;
                    float px = c + vx * k, py = c + vy * k;
                    float vl = (float)Math.Sqrt(vx * vx + vy * vy);
                    if (vl * k > Rin)
                        using (var p = new Pen(Color.FromArgb(160, 255, 255, 255), 2f)) g.DrawLine(p, c + vx / vl * Rin, c + vy / vl * Rin, px, py);
                    using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, px - 7, py - 7, 14, 14);
                    using (var p = new Pen(Color.FromArgb(214, 32, 44), 3f)) g.DrawEllipse(p, px - 7, py - 7, 14, 14);
                }
            }
            protected override void OnPaintBackground(PaintEventArgs e) { }
            Bitmap baseBmp; string baseKey;
            public void RefreshIcons() { baseKey = null; Invalidate(); } // bajo un icono: rehacer la base
            void DrawRim(Graphics g, List<Seg> segs, float per, float c, RectangleF outer)
            {
                if (segs.Count > 1 && segs.Any(s => s.Group >= 0))
                    using (var p = new Pen(Color.FromArgb(225, 215, 205), 3f))
                        for (int i = 0; i < segs.Count; i++)
                        {
                            int prev = (i + segs.Count - 1) % segs.Count;
                            if (segs[i].Group == segs[prev].Group) continue;
                            double a = (-90 - per / 2 + i * per) * Math.PI / 180;
                            g.DrawLine(p, c + (float)(Math.Cos(a) * Rin), c + (float)(Math.Sin(a) * Rin), c + (float)(Math.Cos(a) * R), c + (float)(Math.Sin(a) * R));
                        }
                using (var p = new Pen(Color.FromArgb(90, 84, 88), 2f)) g.DrawEllipse(p, outer);
            }
            // una porcion (hov: cual esta marcada, o -1)
            // centro de "cual sale": sin apuntar, lo que entra; apuntando, el que sale tachado -> el que entra
            void DrawSwapCenter(Graphics g, float c, List<Seg> segs)
            {
                var neu = Cur.Swap;
                var ni = ov.GetIcon(neu.Icon);
                var white = Color.FromArgb(235, 230, 225);
                const TextFormatFlags cf = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                if (hover >= 0 && hover < segs.Count && segs[hover].E != null)
                {
                    var oi = ov.GetIcon(segs[hover].E.Icon);
                    float s = 50, ox = c - 62, nx = c + 12, iy = c - 68;
                    if (oi != null)
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.45f };
                        using (var ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(oi, new Rectangle((int)ox, (int)iy, (int)s, (int)s), 0, 0, oi.Width, oi.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                    using (var p = new Pen(Color.FromArgb(230, 50, 60), 5f)) { g.DrawLine(p, ox + 8, iy + 8, ox + s - 8, iy + s - 8); g.DrawLine(p, ox + s - 8, iy + 8, ox + 8, iy + s - 8); }
                    if (ni != null) g.DrawImage(ni, nx, iy, s, s);
                    TextRenderer.DrawText(g, "→", Dbd.Head(14f), new Rectangle((int)(ox + s - 4), (int)iy, (int)(nx - ox - s + 8), (int)s), white, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, L("sale ", "out: ") + ov.DName(segs[hover].E), Dbd.Cond(10.5f), new Rectangle((int)(c - Rin + 12), (int)(c - 15), 2 * Rin - 24, 18), Color.FromArgb(240, 120, 120), cf);
                    TextRenderer.DrawText(g, L("entra ", "in: ") + ov.DName(neu), Dbd.Cond(10.5f), new Rectangle((int)(c - Rin + 12), (int)(c + 3), 2 * Rin - 24, 18), Color.FromArgb(150, 220, 150), cf);
                }
                else
                {
                    if (ni != null) g.DrawImage(ni, c - 25, c - 70, 50, 50);
                    TextRenderer.DrawText(g, L("entra ", "in: ") + ov.DName(neu), Dbd.Cond(10.5f), new Rectangle((int)(c - Rin + 12), (int)(c - 17), 2 * Rin - 24, 18), Color.FromArgb(150, 220, 150), cf);
                    TextRenderer.DrawText(g, Cur.Title, Dbd.Head(12f), new Rectangle((int)(c - Rin + 12), (int)(c + 2), 2 * Rin - 24, 22), white, cf);
                }
            }
            void DrawSeg(Graphics g, int i, int hov, List<Seg> segs, int n, float per, float c, RectangleF outer, RectangleF inner)
            {
                    float a0 = -90 - per / 2 + i * per;
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddArc(outer, a0, per);
                        path.AddArc(inner, a0 + per, -per);
                        path.CloseFigure();
                        Color? rc = RarColor(segs[i].E);
                        Image photo = IsPhoto(segs[i]) ? ov.GetIcon(segs[i].E != null ? segs[i].E.Icon : segs[i].Icon) : null;
                        if (photo != null)
                        {
                            // mapas: la foto llena toda la porcion (recortada a su forma), oscurecida para que se lea el nombre
                            var bb = path.GetBounds();
                            g.DrawImageUnscaled(SegPhoto(photo, path, n, i), (int)Math.Floor(bb.X), (int)Math.Floor(bb.Y));
                            using (var b = new SolidBrush(i == hov ? Color.FromArgb(110, 200, 24, 36) : segs[i].Marked ? Color.FromArgb(90, 120, 10, 20) : Color.FromArgb(70, 0, 0, 0)))
                                g.FillPath(b, path);
                        }
                        else if (i == hov)
                            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(outer, Color.FromArgb(120, 12, 20), Color.FromArgb(205, 28, 40), 90f)) g.FillPath(b, path);
                        else if (rc != null)
                            using (var b = new SolidBrush(Color.FromArgb(segs[i].Marked ? 95 : 60, rc.Value))) g.FillPath(b, path);
                        else if (segs[i].Marked)
                            using (var b = new SolidBrush(Color.FromArgb(46, 24, 28))) g.FillPath(b, path);
                        else if (segs[i].E == null && segs[i].Icon != null && segs[i].Group == 1)
                            using (var b = new SolidBrush(Color.FromArgb(38, 30, 34))) g.FillPath(b, path); // categorias: un poco distintas de los perks
                        if (rc != null)
                            using (var p = new Pen(rc.Value, 7f)) g.DrawArc(p, c - R + 4, c - R + 4, 2 * R - 8, 2 * R - 8, a0 + 0.8f, per - 1.6f);
                        if (rc != null && segs[i].E.K == Kind.Perk)
                        {
                            double ma = (a0 + per / 2) * Math.PI / 180;
                            string tl = PerkTier(segs[i].E);
                            var tf = Dbd.Head(11f);
                            var ts = TextRenderer.MeasureText(tl, tf);
                            float tx = c + (float)(Math.Cos(ma) * (R - 20)), ty = c + (float)(Math.Sin(ma) * (R - 20));
                            TextRenderer.DrawText(g, tl, tf, new Point((int)(tx - ts.Width / 2f), (int)(ty - ts.Height / 2f)), rc.Value, TextFormatFlags.NoPrefix);
                        }
                        using (var p = new Pen(Color.FromArgb(70, 66, 70), 1.2f)) g.DrawPath(p, path);
                    }
                    // contenido en el medio de la seccion
                    double mid = (a0 + per / 2) * Math.PI / 180, rm = (R + Rin) / 2.0 + (n > 12 ? 18 : 0);
                    float x = c + (float)(Math.Cos(mid) * rm), y = c + (float)(Math.Sin(mid) * rm);
                    var s = segs[i];
                    Image icon = s.E != null ? ov.GetIcon(s.E.Icon) : s.Icon != null ? ov.GetIcon(s.Icon) : null;
                    Color fg = i == hov ? Color.White : Color.FromArgb(222, 216, 210);
                    if (icon != null && IsPhoto(s))
                    {
                        // nombre sobre la foto, con sombra
                        string nm = s.E != null ? ov.DName(s.E) : s.Big;
                        var nf = Dbd.Head(n > 12 ? 10f : 12f);
                        int tw = n > 12 ? 92 : 130;
                        var tr = new Rectangle((int)(x - tw / 2f), (int)(y - 22), tw, 44);
                        var tff = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                        foreach (var o in new[] { new Point(1, 1), new Point(-1, 1), new Point(1, -1), new Point(-1, -1), new Point(0, 2) })
                            TextRenderer.DrawText(g, nm, nf, new Rectangle(tr.X + o.X, tr.Y + o.Y, tr.Width, tr.Height), Color.Black, tff);
                        TextRenderer.DrawText(g, nm, nf, tr, Color.White, tff);
                    }
                    else if (s.E == null && (icon != null || s.Glyph != null))
                    {
                        // primera rueda: icono (o simbolo) grande y el nombre abajo; con muchas secciones, chico y sin nombre
                        if (icon != null && n > 12 && Cur.Title == L("REINO", "REALM"))
                        {
                            // reinos: foto y el nombre chico abajo (son muchos para el nombre grande)
                            Fit(g, icon, x, y - 10, 48);
                            TextRenderer.DrawText(g, s.Big, Dbd.Cond(8.5f), new Rectangle((int)x - 46, (int)y + 10, 92, 28), fg,
                                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        }
                        else if (icon != null && n > 12) Fit(g, icon, x, y, 48);
                        else if (n > 12)
                        {
                            // simbolo con muchas secciones (ej. "Un mapa", "poco usados"): mas chico, y el texto se parte para entrar
                            using (var gf = new Font("Segoe UI Symbol", 18f))
                            {
                                var gs = TextRenderer.MeasureText(s.Glyph, gf);
                                TextRenderer.DrawText(g, s.Glyph, gf, new Point((int)(x - gs.Width / 2f), (int)(y - 14 - gs.Height / 2f)), fg, TextFormatFlags.NoPrefix);
                            }
                            TextRenderer.DrawText(g, s.Big, Dbd.Head(10f), new Rectangle((int)x - 44, (int)y, 88, 34), fg,
                                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        }
                        else
                        {
                            if (icon != null) Fit(g, icon, x, y - 10, 76);
                            else
                                using (var gf = new Font("Segoe UI Symbol", 30f))
                                {
                                    var gs = TextRenderer.MeasureText(s.Glyph, gf);
                                    TextRenderer.DrawText(g, s.Glyph, gf, new Point((int)(x - gs.Width / 2f), (int)(y - 12 - gs.Height / 2f)), fg, TextFormatFlags.NoPrefix);
                                }
                            var lf = Dbd.Head(13f);
                            var ls = TextRenderer.MeasureText(s.Big, lf);
                            TextRenderer.DrawText(g, s.Big, lf, new Point((int)(x - ls.Width / 2f), (int)(y + 30)), fg, TextFormatFlags.NoPrefix);
                        }
                    }
                    else if (s.E != null)
                    {
                        // icono grande y el nombre abajo (sin icono, como los killers: solo el nombre)
                        int sz = n > 16 ? 56 : n > 12 ? 58 : 80, tw = n > 12 ? 84 : 118;
                        // muchos (los addons de un killer): solo iconos; el nombre sale al centro al pasar el mouse
                        if (icon != null && n > 16) { Fit(g, icon, x, y, sz); goto drawn; }
                        if (icon != null) Fit(g, icon, x, y - 10, sz);
                        var nr = icon != null ? new Rectangle((int)(x - tw / 2f), (int)(y + sz / 2f - 16), tw, 30) : new Rectangle((int)(x - tw / 2f), (int)(y - 26), tw, 52);
                        TextRenderer.DrawText(g, ov.DName(s.E), icon != null ? Dbd.Cond(n > 12 ? 7.5f : 8.5f) : Dbd.Head(n > 12 ? 10f : 12f), nr, fg,
                                              TextFormatFlags.HorizontalCenter | (icon != null ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter) | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        drawn: ;
                    }
                    else if (s.Big != null)
                    {
                        var f = Dbd.Head(s.Big.Length <= 2 ? (n > 18 ? 18f : 22f) : 15f);
                        var tsz = TextRenderer.MeasureText(s.Big, f);
                        TextRenderer.DrawText(g, s.Big, f, new Point((int)(x - tsz.Width / 2f), (int)(y - tsz.Height / 2f)), fg, TextFormatFlags.NoPrefix);
                        if (s.Small != null)
                        {
                            var fs = Dbd.Cond(8f);
                            var ssz = TextRenderer.MeasureText(s.Small, fs);
                            TextRenderer.DrawText(g, s.Small, fs, new Point((int)(x - ssz.Width / 2f), (int)(y + tsz.Height / 2f - 2)), Color.FromArgb(150, 140, 138), TextFormatFlags.NoPrefix);
                        }
                    }
                    if (s.Marked)
                        using (var b = new SolidBrush(Color.FromArgb(214, 32, 44)))
                            g.FillPolygon(b, Dbd.Diamond(x + (n > 12 ? 20 : 26), y - (n > 12 ? 20 : 26), 5));
            }

            void PaintWheel(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Key);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float c = D / 2f;
                var segs = Cur.Segs;
                int n = Math.Max(1, segs.Count);
                float per = 360f / n;
                var outer = new RectangleF(c - R, c - R, 2 * R, 2 * R);
                var inner = new RectangleF(c - Rin, c - Rin, 2 * Rin, 2 * Rin);
                // fondo de la rueda
                ShowTiers = ov.cfg.Get("RuedaTiers", "si") != "no";
                // base del nivel (porciones sin marcar): se dibuja una vez; al mover el mouse solo se rehace la porcion marcada
                string bkey = stack.Count + "|" + Cur.GetHashCode() + "|" + ShowTiers;
                if (baseBmp == null || baseKey != bkey)
                {
                    if (baseBmp == null) baseBmp = new Bitmap(D, D);
                    using (var bg = Graphics.FromImage(baseBmp))
                    {
                        bg.Clear(Key);
                        bg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        bg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        using (var b = new SolidBrush(Color.FromArgb(22, 20, 22))) bg.FillEllipse(b, outer);
                        for (int i = 0; i < segs.Count; i++) DrawSeg(bg, i, -1, segs, n, per, c, outer, inner);
                        DrawRim(bg, segs, per, c, outer);
                    }
                    baseKey = bkey;
                }
                g.DrawImageUnscaled(baseBmp, 0, 0);
                if (hover >= 0 && hover < segs.Count) { DrawSeg(g, hover, hover, segs, n, per, c, outer, inner); DrawRim(g, segs, per, c, outer); }
                // centro: lo que esta bajo el mouse (o el titulo) y como volver
                using (var b = new SolidBrush(hover == -2 ? Color.FromArgb(40, 30, 32) : Color.FromArgb(12, 11, 12))) g.FillEllipse(b, inner);
                using (var p = new Pen(Color.FromArgb(90, 84, 88), 2f)) g.DrawEllipse(p, inner);
                if (Cur.Swap != null) DrawSwapCenter(g, c, segs);
                else
                {
                    string label = hover >= 0 && hover < segs.Count ? segs[hover].Label : Cur.Title;
                    var lr = new Rectangle((int)(c - Rin + 10), (int)(c - 34), 2 * Rin - 20, 52);
                    TextRenderer.DrawText(g, label, Dbd.Head(12f), lr, Color.FromArgb(235, 230, 225), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                }
                // debajo de la rueda: que hace (por ahora, las ofrendas)
                DescEsNow = ov.DescEs;
                StatusMode = ov.cfg.Get("EstadosRueda", "ambos");
                string brief = Cur.Swap == null && hover >= 0 && hover < segs.Count && ov.cfg.Get("RuedaDescripcion", "si") != "no" ? BriefOf(segs[hover].E) : null;
                if (brief != null)
                {
                    // los numeros ("+20 %", "(10 s)", "6 m") resaltados: se leen de un vistazo
                    var f = Dbd.Cond(12f);
                    var fb = Dbd.Head(12.5f);
                    const TextFormatFlags tf = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                    brief = Iconize(brief);
                    var words = Regex.Matches(brief, @"\(?\u0001\w+\u0001\S*|\(?[+−]?\d[\d.,]*(?:\s?(?:%|m/s|m|s|grados|degrees)(?!\p{L}))?\)?[.,;]?|\S+").Cast<Match>().Select(mm => mm.Value).ToList();
                    int maxW = D - 72, space = Math.Max(3, TextRenderer.MeasureText(g, "a a", f, Size.Empty, tf).Width - TextRenderer.MeasureText(g, "aa", f, Size.Empty, tf).Width), lineH = f.Height + 2;
                    var lines = new List<List<Tuple<string, bool, int>>> { new List<Tuple<string, bool, int>>() };
                    int cw = 0;
                    foreach (var w in words)
                    {
                        bool hi = Regex.IsMatch(w, @"^\(?[+−]?\d");
                        int ww = TextRenderer.MeasureText(g, w, hi ? fb : f, Size.Empty, tf).Width;
                        var sm = StatusTok.Match(w);
                        if (sm.Success)
                        {
                            // icono de estado (si todavia no bajo: la palabra)
                            var ic = ov.GetIcon("FulliconStatusEffects_" + sm.Groups[2].Value + ".png");
                            string pre = sm.Groups[1].Value, post = sm.Groups[3].Value, word = ic == null || StatusMode == "ambos" ? StatusWord(sm.Groups[2].Value) : "";
                            ww = TextRenderer.MeasureText(g, pre + word + post, f, Size.Empty, tf).Width + (ic != null ? lineH + 4 : 0);
                        }
                        if (cw > 0 && cw + space + ww > maxW) { lines.Add(new List<Tuple<string, bool, int>>()); cw = 0; }
                        if (cw > 0) cw += space;
                        lines[lines.Count - 1].Add(Tuple.Create(w, hi, ww));
                        cw += ww;
                    }
                    if (lines.Count > 4) lines = lines.Take(4).ToList();
                    int widest = lines.Max(l => l.Sum(t => t.Item3) + space * Math.Max(0, l.Count - 1));
                    int bw = Math.Min(D - 40, widest + 32), bh = lines.Count * lineH + 16;
                    var box = new Rectangle((D - bw) / 2, D + 4, bw, bh);
                    using (var b = new SolidBrush(Color.FromArgb(20, 18, 20))) g.FillRectangle(b, box);
                    using (var p = new Pen(Color.FromArgb(150, 28, 40), 1.5f)) g.DrawRectangle(p, box);
                    for (int li = 0; li < lines.Count; li++)
                    {
                        var l = lines[li];
                        int lw = l.Sum(t => t.Item3) + space * Math.Max(0, l.Count - 1), x0 = (D - lw) / 2, y0 = box.Y + 8 + li * lineH;
                        foreach (var t in l)
                        {
                            var sm = StatusTok.Match(t.Item1);
                            if (sm.Success)
                            {
                                var ic = ov.GetIcon("FulliconStatusEffects_" + sm.Groups[2].Value + ".png");
                                int xx = x0;
                                string pre = sm.Groups[1].Value, post = sm.Groups[3].Value;
                                if (pre.Length > 0) { TextRenderer.DrawText(g, pre, f, new Point(xx, y0), Color.FromArgb(225, 220, 215), tf); xx += TextRenderer.MeasureText(g, pre, f, Size.Empty, tf).Width; }
                                if (ic != null) { g.DrawImage(ic, xx, y0 - 2, lineH + 2, lineH + 2); xx += lineH + 4; }
                                if (ic == null || StatusMode == "ambos") { string word = StatusWord(sm.Groups[2].Value); TextRenderer.DrawText(g, word, f, new Point(xx, y0), Color.FromArgb(225, 220, 215), tf); xx += TextRenderer.MeasureText(g, word, f, Size.Empty, tf).Width; }
                                if (post.Length > 0) TextRenderer.DrawText(g, post, f, new Point(xx, y0), Color.FromArgb(225, 220, 215), tf);
                            }
                            else
                            TextRenderer.DrawText(g, t.Item1, t.Item2 ? fb : f, new Point(x0, y0 + (t.Item2 ? -1 : 0)), t.Item2 ? Color.FromArgb(255, 196, 92) : Color.FromArgb(225, 220, 215), tf);
                            x0 += t.Item3 + space;
                        }
                    }
                }
                string sub = Pad ? ov.PadText(ov.PadMask("PadElegir", "R2")) + L(": elegir · al centro: atrás", ": pick · centered: back") : Gesture ? (ClickMode ? L("clic: elegir · derecho: atrás", "click: pick · right: back") : L("empujá: entrar · soltá: elegir", "push: enter · release: pick"))
                           : stack.Count > 1 ? L("clic aquí o derecho: atrás", "click here or right: back") : L("clic derecho: cerrar", "right-click: close");
                TextRenderer.DrawText(g, sub, Dbd.Cond(8.5f), new Rectangle((int)(c - Rin + 8), (int)(c + 22), 2 * Rin - 16, 20), Color.FromArgb(140, 130, 128), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
        }

        void SetupTray()
        {
            trayRed = TrayIcon(Color.FromArgb(190, 22, 34));
            trayGreen = TrayIcon(Color.FromArgb(40, 150, 70));
            tray = new NotifyIcon { Icon = trayRed, Text = AppName, Visible = true };
            // doble clic en el icono: configuracion
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(null); };
            var menu = new ContextMenuStrip();
            // arriba de todo, la version (para saber si ya se actualizo)
            menu.Items.Add(new ToolStripMenuItem(AppName + L(" · versión ", " · version ") + Updater.AppVersion) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var cfgItem = new ToolStripMenuItem(L("Configuración…", "Settings…"), null, (s, e) => OpenSettings(null));
            cfgItem.Font = new Font(cfgItem.Font, FontStyle.Bold);
            menu.Items.Add(cfgItem);
            var typeItem = new ToolStripMenuItem(L("Escribir…", "Type…"));
            typeItem.Click += (s, e) => OpenTypeBox();
            menu.Items.Add(typeItem);
            menu.Items.Add(L("Rueda…", "Wheel…"), null, (s, e) => ToggleWheel());
            menu.Items.Add(L("Borrar lista", "Clear list"), null, (s, e) => ClearWithUndo());
            // lo de siempre de la sala queda a mano; el resto esta en Configuracion > Sala
            var roomMenu = new ToolStripMenuItem(L("Sala compartida", "Shared room"));
            roomLabel = new ToolStripMenuItem(L("Sin sala", "No room")) { Enabled = false };
            roomMenu.DropDownItems.Add(roomLabel);
            roomMenu.DropDownItems.Add("-");
            autoItem = new ToolStripMenuItem(L("Automática con mi grupo de Steam", "Automatic with my Steam party")) { Checked = AutoRoom };
            autoItem.Click += (s, e) => SetAuto(!AutoRoom);
            roomMenu.DropDownItems.Add(autoItem);
            roomMenu.DropDownItems.Add("-");
            roomMenu.DropDownItems.Add(L("Crear sala con código", "Create room with a code"), null, (s, e) => CreateRoom());
            roomMenu.DropDownItems.Add(L("Unirse con código…", "Join with a code…"), null, (s, e) => JoinRoom());
            roomCopy = new ToolStripMenuItem(L("Copiar código", "Copy code"), null, (s, e) =>
            {
                try { Clipboard.SetText(cfg.Get("Sala", "")); Feedback(L("código copiado: ", "code copied: ") + cfg.Get("Sala", ""), Color.Silver, 3000); } catch { }
            });
            roomMenu.DropDownItems.Add(roomCopy);
            roomLeave = new ToolStripMenuItem(L("Salir de la sala con código", "Leave the code room"), null, (s, e) => LeaveRoom());
            roomMenu.DropDownItems.Add(roomLeave);
            menu.Items.Add(roomMenu);
            // estos ya no estan en el menu (estan en la ventana), pero otras partes los actualizan
            roomLineItem = new ToolStripMenuItem(L("Mostrar en la lista si estoy en sala", "Show on the list when I'm in a room")) { Checked = ShowRoomLine };
            namesItem = new ToolStripMenuItem(L("Mostrar nombres", "Show names")) { Checked = showNames };
            listenItem = new ToolStripMenuItem(L("Escuchar Discord (prueba)", "Listen to Discord (beta)"));
            handsItem = new ToolStripMenuItem(L("Hablar sin tecla (prueba)", "Talk without a key (beta)"));
            menu.Opening += (s, e) => { typeItem.Text = L("Escribir… (", "Type… (") + typeKeyName + ")"; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L("Buscar actualización", "Check for updates"), null, (s, e) => { if (updating) return; Feedback(L("Buscando actualización…", "Checking for updates…"), Color.FromArgb(200, 200, 210), 4000); CheckUpdate(true); });
            menu.Items.Add(L("Salir", "Exit"), null, (s, e) => Close());
            tray.ContextMenuStrip = menu;
        }

        void Feedback(string t, Color c, int ms)
        {
            feedback = t; feedbackColor = c; feedbackUntil = Environment.TickCount + ms;
            Relayout();
        }

        void Changed(string what)
        {
            EndSample(); // si llega algo de verdad, el ejemplo se va
            st.Save(Path.Combine(dir, "estado.txt"));
            Feedback(what, Color.FromArgb(150, 220, 150), 5000);
        }

        // las teclas (V, T, G, H) solo cuentan con DBD en primer plano: escribiendo en Discord, el navegador, etc. no hacen nada.
        // TeclasSoloEnDBD=no las vuelve globales (para probar sin el juego).
        bool keysGate;
        bool KeysOnlyInDbd { get { return !Regex.IsMatch(cfg.Get("TeclasSoloEnDBD", "si"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }
        bool HotKey(int vk) { return vk != 0 && keysGate && (GetAsyncKeyState(vk) & 0x8000) != 0; }

        // ---- sin relojes fijos ----
        // "pump" es un reloj de un solo disparo: corre cada 25 ms solo mientras tenes una tecla apretada (o justo
        // despues de soltarla), y si no, se programa para el proximo vencimiento (que se borre un mensaje, etc.).
        // Sin nada pendiente queda apagado.
        System.Windows.Forms.Timer pump, topLater;
        int pumpDue;
        volatile bool kickQueued, watchOk;
        bool steamAuto, cursorShown;

        void Kick()
        {
            kickQueued = false;
            if (pump == null) return;
            int now = Environment.TickCount;
            if (pump.Enabled && pumpDue - (now + 25) <= 0) return;
            pump.Stop(); pump.Interval = 25; pumpDue = now + 25; pump.Start();
        }

        void PumpTick()
        {
            pump.Stop();
            KeyTick();
            HoverTick();
            Schedule();
        }

        static bool IsMouseVk(int vk) { return vk == 1 || vk == 2 || (vk >= 4 && vk <= 6); }

        void Schedule()
        {
            if (pump == null) return;
            bool boxOpen = typeBox != null && typeBox.Visible;
            // un boton del mouse como atajo: Windows no avisa sin escuchar cada movimiento, asi que ahi se mira cada 25 ms
            bool mouseKeys = IsMouseVk(vkType) || IsMouseVk(vkTalk) || IsMouseVk(vkClear) || IsMouseVk(vkHelp) || IsMouseVk(vkNames);
            bool fast = !watchOk || mouseKeys || typePending || talking || releaseAt >= 0 || clearDown || helpShown || namesDown || wheelDown || (typeKeyDown && !boxOpen);
            int now = Environment.TickCount, iv;
            if (fast) iv = 25;
            else
            {
                long best = long.MaxValue;
                if (micRestartAt >= 0) best = Math.Min(best, (long)(micRestartAt - now));
                if (feedback.Length > 0) best = Math.Min(best, (long)(feedbackUntil - now));
                if (heardLine.Length > 0) best = Math.Min(best, (long)(heardUntil - now));
                if (st.Pick != null) best = Math.Min(best, (long)(st.PickUntil - now));
                if (hoverE != null && !(tip != null && tip.Visible && tip.Current == hoverE)) best = Math.Min(best, (long)(hoverSince + 260 - now));
                if (best == long.MaxValue) { pump.Stop(); return; }
                iv = (int)Math.Max(15, Math.Min(best + 5, 3600000));
            }
            if (pump.Enabled && pumpDue - (now + iv) <= 0) return; // ya va a mirar a tiempo
            pump.Stop(); pump.Interval = iv; pumpDue = now + iv; pump.Start();
        }

        // gancho de teclado que solo mira (no se traga nada): en su propio hilo, para que el teclado nunca espere a la app
        IntPtr watchHook;
        LowLevelKeyboardProc watchProc;

        void StartKeyWatch()
        {
            new Thread(() =>
            {
                watchProc = WatchProc;
                watchHook = SetWindowsHookEx(13, watchProc, GetModuleHandle(null), 0);
                int err = Marshal.GetLastWin32Error();
                watchOk = watchHook != IntPtr.Zero;
                try { BeginInvoke((Action)(() => { if (!watchOk) DebugEvent("teclas: sin gancho (error " + err + "), miro cada 25 ms como antes"); Schedule(); })); } catch { }
                if (watchOk) Application.Run();
            }) { IsBackground = true, Name = "teclas" }.Start();
        }

        // ---- leer el mapa con un boton del mouse (por defecto el del medio). Windows no avisa los clics sin escuchar
        // tambien cada movimiento, asi que el gancho va en su propio hilo y solo mira (no se traga nada): el juego recibe
        // el clic igual. Solo se instala si el atajo es un boton del mouse.
        IntPtr mouseHook; LowLevelKeyboardProc mouseProc;
        void StartMouseWatch()
        {
            if (mouseHook != IntPtr.Zero || mouseProc != null || !IsMouseVk(vkMapOcr)) return;
            mouseProc = MouseWatchProc;
            new Thread(() =>
            {
                mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0);
                int err = Marshal.GetLastWin32Error();
                bool ok = mouseHook != IntPtr.Zero;
                try { BeginInvoke((Action)(() => DebugEvent(ok ? "mouse: escuchando " + cfg.Get("TeclaMapaFoto", "MButton") + " para leer el mapa" : "mouse: sin gancho (error " + err + "), el clic para leer el mapa no anda"))); } catch { }
                if (!ok) return;
                Application.Run();
            }) { IsBackground = true, Name = "mouse" }.Start();
        }

        IntPtr MouseWatchProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam, vk = 0;
                if (msg == 0x207) vk = 4; // WM_MBUTTONDOWN
                else if (msg == 0x20B) vk = (Marshal.ReadInt32(lParam, 8) >> 16) == 1 ? 5 : 6; // WM_XBUTTONDOWN (laterales)
                if (vk != 0 && vk == vkMapOcr) try { BeginInvoke((Action)(() => ReadMapFromScreen(false))); } catch { }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct WinPoint { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out WinRect r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref WinPoint p);

        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);

        // un clic: copia dos rincones del juego y los lee con el OCR de Windows.
        //  - abajo a la izquierda: el nombre del mapa al empezar -> carga el plano
        //  - arriba a la derecha: los avisos de estado ("EXPOSED" / "HEX: NO ONE ESCAPES DEATH") -> anota ese perk o addon
        // Las ventanas de la app se sacan de la captura un instante (si no, la lista tapa los avisos).
        public void ReadMapFromScreen(bool anyWindow)
        {
            if (mapOcrBusy) return;
            bool dbd = DbdInFront();
            if (!anyWindow)
            {
                if (KeysOnlyInDbd && !dbd) return;
                if (!ModsMatch(vkMapOcr, mapOcrSh, mapOcrCt, mapOcrAl)) return;
                if (typeBox != null && typeBox.Visible) return;
            }
            Rectangle game = Screen.PrimaryScreen.Bounds;
            if (dbd)
            {
                var h = GetForegroundWindow(); WinRect r; var p = new WinPoint();
                if (GetClientRect(h, out r) && ClientToScreen(h, ref p) && r.R > 200 && r.B > 200) game = new Rectangle(p.X, p.Y, r.R, r.B);
            }
            mapOcrBusy = true;
            // fuera de la captura (Windows 10 2004+); si no se puede, se esconden por un instante
            var mine = Application.OpenForms.Cast<Form>().Where(f => f.Visible && !f.IsDisposed).ToList();
            var hidden = new List<Form>();
            int excl = 0;
            foreach (var f in mine)
            {
                if (SetWindowDisplayAffinity(f.Handle, 0x11)) excl++;
                else { f.Hide(); hidden.Add(f); }
            }
            var later = new System.Windows.Forms.Timer { Interval = mine.Count > 0 ? 100 : 1 }; // que la pantalla se redibuje sin ellas
            later.Tick += (s, e) =>
            {
                later.Stop(); later.Dispose();
                Bitmap capMap = null, capStat = null; string capErr = null;
                try
                {
                    var rm = MapReader.Region(game); var rs = MapReader.StatusRegion(game);
                    capMap = new Bitmap(rm.Width, rm.Height); using (var g = Graphics.FromImage(capMap)) g.CopyFromScreen(rm.Location, Point.Empty, rm.Size);
                    capStat = new Bitmap(rs.Width, rs.Height); using (var g = Graphics.FromImage(capStat)) g.CopyFromScreen(rs.Location, Point.Empty, rs.Size);
                }
                catch (Exception ex) { capErr = ex.Message; }
                foreach (var f in mine) if (!f.IsDisposed) { if (hidden.Contains(f)) f.Show(); else SetWindowDisplayAffinity(f.Handle, 0); }
                if (capErr != null)
                {
                    mapOcrBusy = false;
                    if (capMap != null) capMap.Dispose();
                    if (capStat != null) capStat.Dispose();
                    DebugEvent("leer pantalla: no pude copiar la pantalla: " + capErr);
                    return;
                }
                string how = mine.Count == 0 ? "" : " (ventanas propias: " + excl + " fuera de la captura" + (hidden.Count > 0 ? ", " + hidden.Count + " escondidas" : "") + ")";
                var killer = st.Killer;
                new Thread(() => ReadScreenWork(capMap, capStat, killer, how)) { IsBackground = true, Name = "leer-pantalla" }.Start();
            };
            later.Start();
        }

        void ReadScreenWork(Bitmap capMap, Bitmap capStat, Entry killer, string how)
        {
            string[] mapLines = null; var statLines = new List<string>(); bool noOcr = false;
            string mapName = null, whyMap = "", whySrc = "", err = null;
            var srcs = new List<Tuple<Entry, double>>();
            var sw = Stopwatch.StartNew();
            try
            {
                using (var big = MapReader.Prepare(capMap)) mapLines = WinOcr.Read(big);
                if (mapLines == null) noOcr = true;
                else
                {
                    mapName = MapReader.Match(cat, mapLines, out whyMap);
                    string[] a, b;
                    using (var big = MapReader.Prepare(capStat)) a = WinOcr.Read(big);
                    using (var con = MapReader.Contrast(capStat)) b = WinOcr.Read(con);
                    if (a != null) statLines.AddRange(a);
                    if (b != null) statLines.AddRange(b);
                    srcs = MapReader.MatchSources(cat, statLines, killer, out whySrc);
                }
                try
                {
                    Directory.CreateDirectory(Path.Combine(dir, "logs"));
                    capMap.Save(Path.Combine(dir, "logs", "mapa-ocr.png"));
                    capStat.Save(Path.Combine(dir, "logs", "estados-ocr.png"));
                }
                catch { }
            }
            catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
            finally { capMap.Dispose(); capStat.Dispose(); }
            long ms = sw.ElapsedMilliseconds;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    mapOcrBusy = false;
                    DebugEvent("leer pantalla (" + ms + " ms)" + how + ": " + (err ?? (noOcr ? "sin OCR en este Windows" :
                        "mapa [" + string.Join(" | ", mapLines) + "] -> " + whyMap + " · estados [" + string.Join(" | ", statLines) + "] -> " + whySrc)));
                    if (err != null) return;
                    if (noOcr)
                    {
                        Feedback(L("este Windows no tiene el lector de texto (OCR) para leer la pantalla", "this Windows has no text reader (OCR) to read the screen"), Color.Silver, 4000);
                        return;
                    }
                    var said = new List<string>();
                    if (mapName != null)
                    {
                        if (mapName != mapShown || mapWin == null || !mapWin.Visible) ShowMap(mapName, true);
                        said.Add(L("mapa: ", "map: ") + mapName);
                    }
                    var cur = st.Current();
                    var add = srcs.Select(t => t.Item1).Where(e => !cur.Contains(e) || st.IsDoubt(e)).ToList();
                    if (add.Count > 0)
                    {
                        // lo dice el juego: va seguro (sin "?"); si estaba como sospecha, queda confirmado
                        BoxDo(L("pantalla: ", "screen: ") + string.Join(", ", add.Select(e => e.Name)),
                              () => string.Join(" · ", add.Select(e => st.AddPicked(e, false))), "pantalla");
                        if (said.Count > 0) Feedback(said[0] + " · " + feedback, Color.FromArgb(150, 220, 150), 5000);
                        return;
                    }
                    if (srcs.Count > 0) said.Add(L("ya anotado: ", "already listed: ") + string.Join(", ", srcs.Select(t => t.Item1.Name)));
                    if (said.Count > 0) Feedback(string.Join(" · ", said), Color.FromArgb(150, 220, 150), 4000);
                    else Feedback(L("no vi nombres en pantalla (mapa: abajo a la izq. al empezar · perks: arriba a la der.)", "no names on screen (map: bottom-left at the start · perks: top-right)"), Color.Silver, 3500);
                }));
            }
            catch { mapOcrBusy = false; }
        }

        bool IsOurVk(int v) { return v != 0 && (v == vkType || v == vkTalk || v == vkClear || v == vkHelp || v == vkNames || v == vkWheel || v == vkUndo || v == vkBuild || (v == vkMapOcr && !IsMouseVk(v)) || (v == 0x1B && wheelOpen)); }

        IntPtr WatchProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int g = vk == 0xA0 || vk == 0xA1 ? 0x10 : vk == 0xA2 || vk == 0xA3 ? 0x11 : vk == 0xA4 || vk == 0xA5 ? 0x12 : vk;
                if ((IsOurVk(vk) || IsOurVk(g)) && !kickQueued)
                {
                    kickQueued = true;
                    try { BeginInvoke((Action)Kick); } catch { kickQueued = false; }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // avisos de Windows: ventana del frente, juego minimizado, cursor (aparece / desaparece / cambia / se mueve)
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        WinEventProc winProc; // referencia viva
        IntPtr moveHook;

        void StartWinEvents()
        {
            winProc = OnWinEvent;
            SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, winProc, 0, 0, 0); // ventana del frente
            SetWinEventHook(0x0016, 0x0016, IntPtr.Zero, winProc, 0, 0, 0); // ventana minimizada
            SetWinEventHook(0x8002, 0x8003, IntPtr.Zero, winProc, 0, 0, 0); // algo aparece / desaparece (filtrado: el cursor)
            SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, winProc, 0, 0, 0); // cambia la forma del cursor
        }

        // los movimientos del mouse solo se escuchan con el cursor visible y la lista en pantalla (para la descripcion
        // al pasar el mouse); en partida DBD esconde el cursor y no llega nada
        void SetMoveHook(bool on)
        {
            if (on == (moveHook != IntPtr.Zero)) return;
            if (on) moveHook = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, winProc, 0, 0, 0);
            else { UnhookWinEvent(moveHook); moveHook = IntPtr.Zero; }
        }

        void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (ev == 0x0003 || ev == 0x0016)
                {
                    if (typeBox != null && typeBox.Visible) KeyTick(); // el cuadro se cierra si el juego dejo de estar al frente
                    TopAll();
                    topLater.Stop(); topLater.Start(); // y otra vez en un rato, por si el juego se pone encima al arrancar
                    if (steamAuto && steamProc == null && DbdInFront()) SteamTick();
                    UpdateTopGuard();
                    HoverTick();
                    return;
                }
                if (idObject != -9) return; // solo el cursor
                if (ev == 0x800B && !cursorShown) return;
                HoverTick();
            }
            catch { }
        }

        // DBD tambien se pone "siempre arriba" y lo repite solo (sin cambiar de ventana): mientras el juego esta al
        // frente, cada 2 s se mira si quedo encima de la lista o del mapa y, solo en ese caso, se los vuelve a subir.
        // Con DBD cerrado o en segundo plano este reloj no corre.
        System.Windows.Forms.Timer topGuard;
        bool topWarned;
        [DllImport("user32.dll", EntryPoint = "GetWindow")] static extern IntPtr GetWindowRel(IntPtr h, uint cmd);
        void UpdateTopGuard()
        {
            if (topGuard == null) { topGuard = new System.Windows.Forms.Timer { Interval = 2000 }; topGuard.Tick += (s, e) => GuardTop(); }
            bool on = DbdInFront();
            if (on && !topGuard.Enabled) topGuard.Start();
            else if (!on && topGuard.Enabled) topGuard.Stop();
        }
        static bool IsAbove(IntPtr a, IntPtr b)
        {
            int n = 0;
            for (var h = GetWindowRel(b, 3); h != IntPtr.Zero && n < 2000; h = GetWindowRel(h, 3), n++) if (h == a) return true; // 3 = la de arriba
            return false;
        }
        void GuardTop()
        {
            if (!DbdInFront()) { topGuard.Stop(); return; }
            var fg = GetForegroundWindow();
            bool under = (Visible && IsAbove(fg, Handle)) || (mapWin != null && mapWin.Visible && IsAbove(fg, mapWin.Handle));
            if (!under) return;
            if (!topWarned) { topWarned = true; DebugEvent("DBD quedó encima de la lista: la vuelvo a subir (se avisa una sola vez)"); }
            TopAll();
        }

        void TopAll()
        {
            if (Visible) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            if (mapWin != null && mapWin.Visible) SetWindowPos(mapWin.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        void KeyTick()
        {
            // escribiendo en el cuadro: las teclas son para el texto (la V, la G o la H no hacen nada)
            if (typeBox != null && typeBox.Visible)
            {
                typeKeyDown = true;
                // se cierra solo (para no quedar tomando el teclado sin que se note):
                // si cambia la ventana del frente o si se minimiza el juego (sin limite de tiempo)
                var fgNow = GetForegroundWindow();
                int nowT = Environment.TickCount;
                if (typeFromGame && typePrevFg != IntPtr.Zero && (fgNow != typePrevFg || IsIconic(typePrevFg))) { HideTypeBox("el juego dejó de estar al frente"); return; }
                if (!typeFromGame)
                {
                    // abierto desde el menu: los primeros 0,7 s el foco se acomoda; despues, si cambia, se cierra
                    if (nowT - typeOpenedAt <= 700) typeRefFg = fgNow;
                    else if (fgNow != typeRefFg) { HideTypeBox("cambió la ventana del frente"); return; }
                }
                return;
            }
            if (keyCapture != null && !keyCapture.IsDisposed && keyCapture.Visible) return; // eligiendo una tecla: no se dispara nada
            keysGate = !KeysOnlyInDbd || DbdInFront();
            bool tk = vkType != 0 && TypeComboDown();
            // se abre al soltar la T: asi la tecla no queda apretada contra el cuadro ni contra el juego
            // (no se pide el cursor visible: DBD dibuja su propio cursor y Windows lo reporta oculto incluso en el lobby)
            if (tk && !typeKeyDown && !talking) typePending = true;
            if (typePending && (GetAsyncKeyState(vkType) & 0x8000) == 0) { typePending = false; if (DbdInFront() || !KeysOnlyInDbd) OpenTypeBox(); }
            typeKeyDown = tk;
            int now = Environment.TickCount;
            // hablar: hay que empezar con la combinacion exacta (Shift+V); despues alcanza con seguir apretando la V
            bool down = HotKey(vkTalk) && (talking || ModsMatch(vkTalk, talkShift, talkCtrl, talkAlt));

            if (mic != null && VoiceOn && !mic.Running && micRestartAt < 0)
            {
                Feedback(L("Micrófono: ", "Microphone: ") + (mic.LastError.Length > 0 ? mic.LastError : L("se cortó", "disconnected")) + L(" (reintento…)", " (retrying…)"), Color.OrangeRed, 6000);
                DebugEvent("micrófono: " + (mic.LastError.Length > 0 ? mic.LastError : "se cortó") + " (reintento en 3 s)");
                micRestartAt = now + 3000;
            }
            if (micRestartAt >= 0 && now - micRestartAt >= 0) { micRestartAt = -1; StartMic(); }

            if (!voice)
            {
                if (down && !talkWarned && VoiceOn) // con la voz apagada a proposito, la tecla no dice nada
                {
                    talkWarned = true;
                    Feedback(installing ? L("instalando la voz, esperá…", "installing voice, wait…") : L("esta PC no tiene Whisper: instalalo desde Configuración → Voz", "Whisper is not installed: Settings → Voice"), Color.Orange, 6000);
                }
                if (!down) talkWarned = false;
                down = false;
            }
            if (down && !talking && releaseAt < 0)
            {
                talking = true;
                recStart = mic != null ? mic.Pos - 16000 * 3 / 10 : 0; // 0,3 s antes de apretar
                Relayout();
            }
            else if (!down && talking)
            {
                talking = false;
                releaseAt = now + 250; // 0,25 s despues de soltar
                Relayout();
            }
            if (releaseAt >= 0 && now - releaseAt >= 0)
            {
                releaseAt = -1;
                long end = mic != null ? mic.Pos : 0;
                ProcessAudio(mic != null ? mic.Get(recStart, end) : new short[0]);
            }

            if (vkClear != 0)
            {
                // mantener 1 s = borrar todo; toque corto despues de borrar = deshacer
                bool c = HotKey(vkClear) && (clearDown || ModsMatch(vkClear, clearSh, clearCt, clearAl));
                if (c && !clearDown) { clearStart = now; clearFired = false; clearHint = false; }
                if (c && !clearFired)
                {
                    int held = now - clearStart;
                    if (held >= 1000) { clearFired = true; ClearWithUndo(); }
                    else if (held >= 250 && !clearHint && HasAnything())
                    {
                        clearHint = true;
                        Feedback(L("mantené ", "hold ") + ClearKeyName + L(" para borrar todo…", " to clear everything…"), Color.FromArgb(240, 170, 80), 1500);
                    }
                }
                if (!c && clearDown && !clearFired && CanUndoClear) UndoClear();
                clearDown = c;
            }
            if (vkHelp != 0)
            {
                bool hk = HotKey(vkHelp) && (helpShown || ModsMatch(vkHelp, helpSh, helpCt, helpAl));
                if (hk != helpShown) { helpShown = hk; Relayout(); }
            }
            if (vkNames != 0)
            {
                bool c = HotKey(vkNames) && (namesDown || ModsMatch(vkNames, namesSh, namesCt, namesAl));
                if (c && !namesDown) SetNames(!showNames);
                namesDown = c;
            }
            // ESC con la rueda abierta: se cierra (no se bloquea: si estas en el menu del juego, ESC tambien lo cierra)
            if (wheelOpen && (GetAsyncKeyState(0x1B) & 0x8000) != 0 && wheelWin != null && !wheelWin.IsDisposed) wheelWin.Hide();
            // deshacer: en cualquier momento (tambien jugando); como las demas teclas, solo con DBD al frente si asi esta configurado
            if (vkUndo != 0)
            {
                bool u = HotKey(vkUndo) && (undoDown || ModsMatch(vkUndo, undoSh, undoCt, undoAl));
                if (u && !undoDown && !(typeBox != null && typeBox.Visible)) UndoLast();
                undoDown = u;
            }
            if (vkMapOcr != 0 && !IsMouseVk(vkMapOcr))
            {
                bool b = HotKey(vkMapOcr) && (mapOcrDown || ModsMatch(vkMapOcr, mapOcrSh, mapOcrCt, mapOcrAl));
                if (b && !mapOcrDown && !(typeBox != null && typeBox.Visible)) ReadMapFromScreen(false);
                mapOcrDown = b;
            }
            if (vkBuild != 0)
            {
                bool b = HotKey(vkBuild) && (buildDown || ModsMatch(vkBuild, buildSh, buildCt, buildAl));
                if (b && !buildDown && !(typeBox != null && typeBox.Visible)) NextBuild();
                buildDown = b;
            }
            if (vkWheel != 0)
            {
                bool c = HotKey(vkWheel) && (wheelDown || ModsMatch(vkWheel, wheelSh, wheelCt, wheelAl));
                if (c != wheelDown)
                {
                    if (cfg.Get("RuedaModo", "clic").StartsWith("gesto"))
                    {
                        if (c)
                        {
                            if (wheelWin == null || wheelWin.IsDisposed) wheelWin = new WheelWindow(this);
                            if (!wheelWin.Visible || !wheelWin.Gesture) wheelWin.Open(true);
                            wheelWin.ClickMode = cfg.Get("RuedaModo", "clic") == "gestoclic";
                            wheelWin.Steer(true);
                        }
                        else if (wheelWin != null && !wheelWin.IsDisposed && wheelWin.Visible) wheelWin.Steer(false);
                    }
                    else if (c) ToggleWheel();
                }
                wheelDown = c;
            }

            if (feedback.Length > 0 && now - feedbackUntil >= 0) { feedback = ""; Relayout(); }
            if (heardLine.Length > 0 && now - heardUntil >= 0) { heardLine = ""; Relayout(); }
        }

        int clearStart, undoUntil;
        bool clearFired, clearHint;
        object undoSnap;
        string ClearKeyName { get { return cfg.Get("TeclaLimpiar", "Shift+G"); } }

        bool HasAnything() { return st.Killer != null || st.Perks.Count > 0 || st.Addons.Count > 0 || st.Offering != null; }

        bool CanUndoClear { get { return undoSnap != null && Environment.TickCount - undoUntil < 0; } }
        void UndoClear()
        {
            if (!CanUndoClear) return;
            var before = st.Snapshot();
            st.Restore(undoSnap);
            undoSnap = null;
            UnrecordLast();
            PublishDiff(before);
            Changed(L("borrado deshecho", "clear undone"));
            DebugEvent("borrado deshecho");
        }
        void ClearWithUndo()
        {
            if (!HasAnything())
            {
                // solo habia un mapa (por ejemplo, leido de la pantalla): se saca el mapa (para todos en la sala)
                if (mapShown != null) { HideMap(true); DebugEvent("mapa sacado con borrar (la lista ya estaba vacía)"); Feedback(L("mapa sacado", "map removed"), Color.FromArgb(150, 220, 150), 2500); return; }
                Feedback(L("la lista ya está vacía", "the list is already empty"), Color.Silver, 2000);
                return;
            }
            undoSnap = st.Snapshot();
            undoUntil = Environment.TickCount + 8000;
            DebugEvent("lista borrada con tecla o menú (antes de borrar)");
            RecordMatch();
            HideMap(false); // los demas lo sacan con el borrado de la lista
            st.Clear();
            st.Save(Path.Combine(dir, "estado.txt"));
            PublishReset();
            Feedback(L("lista borrada · tocá ", "list cleared · tap ") + ClearKeyName + L(" para deshacer", " to undo"), Color.FromArgb(150, 220, 150), 8000);
        }

        // historial.txt: que transcribio Whisper y que hizo (para ajustar el reconocimiento)
        void Log(string text, string what)
        {
            try
            {
                string path = Path.Combine(dir, "historial.txt");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 512 * 1024) File.Delete(path); // que no crezca sin limite
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  \"" + (text ?? "").Trim() + "\"  ->  " + what + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        // ---- logs\debug.log: todo lo necesario para depurar sin capturas (lo lee Claude)
        string lastPromptLogged;

        void Debug(string block)
        {
            try
            {
                string folder = Path.Combine(dir, "logs");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "debug.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                {
                    string old = Path.Combine(folder, "debug.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, block.TrimEnd() + "\r\n\r\n", Encoding.UTF8);
            }
            catch { }
        }

        void DebugEvent(string what)
        {
            Debug("---- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " evento: " + what + "\r\nestado: " + st.Describe());
        }

        // guarda el audio de cada frase (ultimos 200) para poder re-transcribirlo
        string SaveAudio(short[] samples, string stamp)
        {
            try
            {
                string folder = Path.Combine(dir, "logs", "audio");
                Directory.CreateDirectory(folder);
                string name = stamp + ".wav";
                File.WriteAllBytes(Path.Combine(folder, name), Whisper.Wav(samples));
                var files = new DirectoryInfo(folder).GetFiles("*.wav").OrderByDescending(f => f.Name).Skip(200);
                foreach (var f in files) { try { f.Delete(); } catch { } }
                return @"logs\audio\" + name;
            }
            catch (Exception ex) { return "(no se pudo guardar: " + ex.Message + ")"; }
        }

        void ProcessAudio(short[] samples)
        {
            var started = DateTime.Now;
            string stamp = started.ToString("yyyyMMdd-HHmmss");
            double secs = samples.Length / 16000.0;
            if (!serverReady) { Feedback(L("Whisper todavía está cargando", "Whisper is still loading"), Color.Orange, 3000); DebugEvent("audio ignorado: Whisper cargando"); return; }
            if (samples.Length < 16000 * 6 / 10)
            {
                Feedback(L("muy corto, mantené la tecla mientras hablás", "too short, hold the key while you talk"), Color.Orange, 3000);
                DebugEvent("audio ignorado: muy corto (" + secs.ToString("0.00", CultureInfo.InvariantCulture) + " s)");
                return;
            }
            if (busy) { DebugEvent("audio ignorado: todavía procesando el anterior"); return; }
            // sin voz no se manda a Whisper (con silencio inventa frases)
            double peak = 0;
            for (int i = 0; i + 320 <= samples.Length; i += 320)
            {
                double sum = 0;
                for (int j = i; j < i + 320; j++) sum += (double)samples[j] * samples[j];
                peak = Math.Max(peak, Math.Sqrt(sum / 320));
            }
            string audioInfo = "audio: " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s, volumen pico " + (int)peak + " (minimo 700), " + SaveAudio(samples, stamp);
            if (peak < 700)
            {
                Feedback(L("no se escuchó voz (¿micrófono silenciado?)", "no voice heard (is the mic muted?)"), Color.FromArgb(240, 170, 80), 4000);
                DebugEvent("audio descartado por silencio · " + audioInfo);
                return;
            }
            busy = true; Relayout();
            string prompt = st.Prompt();
            if (prompt != lastPromptLogged) { lastPromptLogged = prompt; Debug("---- prompt de Whisper: " + prompt); }
            new Thread(() =>
            {
                string text = null, err = null;
                var t0 = DateTime.Now;
                try { text = Whisper.Transcribe(port, Whisper.Wav(samples), prompt); }
                catch (Exception ex) { err = ex.Message; }
                int ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                BeginInvoke((Action)(() =>
                {
                    busy = false;
                    var sb = new StringBuilder();
                    sb.Append("==== " + started.ToString("yyyy-MM-dd HH:mm:ss") + " ====\r\n");
                    sb.Append(audioInfo + ", Whisper " + ms + " ms\r\n");
                    if (err != null)
                    {
                        Feedback(L("Error de Whisper: ", "Whisper error: ") + err, Color.OrangeRed, 6000);
                        sb.Append("ERROR de Whisper: " + err);
                        Debug(sb.ToString());
                        return;
                    }
                    sb.Append("whisper: \"" + text + "\"\r\n");
                    sb.Append("antes:   " + st.Describe() + "\r\n");
                    // "mostrar nombres" / "ocultar nombres" / "nombres"
                    string nt = Txt.Norm(text);
                    if (Regex.IsMatch(nt, @"\b(?:nombres?|names?)\b") && nt.Split(' ').Length <= 4)
                    {
                        bool v = Regex.IsMatch(nt, @"\b(ocult|sac|quit|escond|sin|hide|off|remove)") ? false
                               : Regex.IsMatch(nt, @"\b(mostr|ver|pon|activ|con|show|on\b)") ? true : !showNames;
                        SetNames(v);
                        Feedback(v ? L("nombres visibles", "names shown") : L("nombres ocultos", "names hidden"), Color.FromArgb(150, 220, 150), 3000);
                        sb.Append("resultado: nombres " + (v ? "visibles" : "ocultos"));
                        Debug(sb.ToString());
                        return;
                    }
                    st.Trace.Clear();
                    var before = st.Snapshot();
                    string what = st.Apply(text);
                    PublishDiff(before);
                    foreach (var line in st.Trace) sb.Append(line + "\r\n");
                    sb.Append("resultado: " + what + "\r\n");
                    sb.Append("despues: " + st.Describe());
                    Debug(sb.ToString());
                    Log(text, what);
                    Heard(KeyName, text, what, false);
                    bool ok = !Lang.Fail(what);
                    if (ok) st.Save(Path.Combine(dir, "estado.txt"));
                    Feedback("“" + text.Trim() + "” → " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), ok ? 5000 : 7000);
                }));
            }) { IsBackground = true }.Start();
        }

        static Color RarityColor(string r)
        {
            switch (r)
            {
                case "common": return Color.FromArgb(190, 160, 120);
                case "uncommon": return Color.FromArgb(235, 200, 80);
                case "rare": return Color.FromArgb(90, 200, 110);
                case "veryrare": return Color.FromArgb(185, 120, 255);
                case "visceral": return Color.FromArgb(255, 80, 100);
                default: return Color.Gainsboro;
            }
        }

        // ---- layout compacto: perks en rombo (como el HUD de superviviente), addons al costado
        class Item { public Rectangle R; public Entry E; public bool Slot, Ghost; public int Num; }
        class TextItem { public string T; public Font F; public Color C; public Point P; public int W; public bool Block, Center; }
        List<Item> items = new List<Item>();
        List<TextItem> texts = new List<TextItem>();
        bool showNames;
        // escala general del widget (iconos, letras, killer): Escala=100 es el tamano normal
        float UiScale { get { return Math.Max(30, Math.Min(300, cfg.GetInt("Escala", 100))) / 100f; } }
        int PerkSize { get { return Math.Max(16, (int)(Math.Max(24, cfg.GetInt("TamanoPerk", 72)) * UiScale)); } }

        void MakeFonts()
        {
            float fs = Math.Max(6f, cfg.GetInt("TamanoLetra", 14) * UiScale);
            fTitle = new Font("Segoe UI Semibold", fs);
            fItem = new Font("Segoe UI", fs);
            fSmall = new Font("Segoe UI", fs * 0.78f);
            killerFont = null;
            if (tip != null) { tip.Hide(); tip = null; hoverE = null; }
        }

        void SetNames(bool v)
        {
            showNames = v;
            cfg.V["Nombres"] = v ? "si" : "no";
            Cfg.Set(Path.Combine(dir, "config.ini"), "Nombres", v ? "si" : "no");
            if (namesItem != null) namesItem.Checked = v;
            Relayout();
        }

        bool inVis;
        void Relayout()
        {
            if (!inVis) { inVis = true; try { UpdateVisibility(cursorShown); } finally { inVis = false; } }
            Schedule();
            items = new List<Item>();
            texts = new List<TextItem>();
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Func<string, Font, Size> M = (t, f) => TextRenderer.MeasureText(t, f, new Size(int.MaxValue, int.MaxValue), flags);
            Color dim = Color.FromArgb(140, 140, 150);
            LoadBuilds();
            var ghosts = GhostPerks(); int gi = 0;
            bool any = st.Perks.Count > 0 || st.Addons.Count > 0 || st.Offering != null || ghosts.Count > 0 || ShowBuildButtons; // el killer no se muestra
            int pad = 8, D = PerkSize, o = D / 2 + 2, A = (int)(D * 0.62);
            int blockW = 0, y = pad;

            // nombre del killer arriba de todo (opcional, tamano configurable)
            TextItem ktext = null;
            if (st.Killer != null && KillerOn)
            {
                var kf = KillerFont();
                var ks = M(st.Killer.Name, kf);
                ktext = new TextItem { T = st.Killer.Name, F = kf, C = Color.FromArgb(235, 95, 95), P = new Point(pad, y), W = ks.Width, Center = true };
                y += ks.Height + 8;
            }
            int top = y, blockX = 0; // blockX: corrimiento del bloque si algo de arriba/abajo es mas ancho

            if (any)
            {
                // nombres (opcional) y notas (siempre: son lo que dijo el usuario, no tienen icono)
                var nameLines = new List<Tuple<string, Color>>();
                if (showNames)
                {
                    string[] arrows = { "▲", "◀", "▶", "▼" }; // arriba, izq, der, abajo
                    Color doubtC = Color.FromArgb(240, 190, 60);
                    for (int i = 0; i < st.Perks.Count && i < 4; i++)
                    {
                        var p = st.Perks[i]; bool d = st.IsDoubt(p);
                        nameLines.Add(Tuple.Create(arrows[i] + " " + DName(p) + (d ? " (?)" : ""), d ? doubtC : Color.WhiteSmoke));
                    }
                    foreach (var a in st.Addons) if (a.Rarity != "nota") nameLines.Add(Tuple.Create("■ " + DName(a) + (st.IsDoubt(a) ? " (?)" : ""), st.IsDoubt(a) ? doubtC : RarityColor(a.Rarity)));
                    var of = st.Offering;
                    if (of != null && of.Rarity != "nota") nameLines.Add(Tuple.Create("✦ " + DName(of) + (st.IsDoubt(of) ? " (?)" : ""), st.IsDoubt(of) ? doubtC : RarityColor(of.Rarity)));
                }
                foreach (var a in st.Addons) if (a.Rarity == "nota") nameLines.Add(Tuple.Create("? " + a.Name + (st.IsDoubt(a) ? " (?)" : ""), st.IsDoubt(a) ? Color.FromArgb(240, 190, 60) : Color.Gainsboro));
                if (st.Offering != null && st.Offering.Rarity == "nota")
                    nameLines.Add(Tuple.Create(L("✦ ofrenda: ", "✦ offering: ") + st.Offering.Name + (st.IsDoubt(st.Offering) ? " (?)" : ""), st.IsDoubt(st.Offering) ? Color.FromArgb(240, 190, 60) : Color.Gainsboro));

                int lh = fSmall.Height + 2, namesW = 0;
                foreach (var nl in nameLines) namesW = Math.Max(namesW, Math.Min(M(nl.Item1, fSmall).Width, 340));
                int namesH = nameLines.Count * lh;
                var realAddons = st.Addons.Where(a => a.Rarity != "nota").ToList();
                int clusterW = 2 * o + D;
                int blockH = Math.Max(clusterW, namesH);

                int x = pad;
                int ny = top + (blockH - namesH) / 2;
                foreach (var nl in nameLines)
                {
                    texts.Add(new TextItem { T = nl.Item1, F = fSmall, C = nl.Item2, P = new Point(x, ny), W = namesW, Block = true });
                    ny += lh;
                }
                if (nameLines.Count > 0) x += namesW + 10;

                int ccx = x + clusterW / 2, ccy = top + blockH / 2;
                // addons en las esquinas libres de la izquierda del rombo: el hueco es un triangulo con catetos D,
                // entra un cuadrado de lado D/2 (el primero arriba, el segundo abajo)
                int aS = (D + 2) / 2 - 2;
                for (int i = 0; i < realAddons.Count && i < 2; i++)
                {
                    int ay = i == 0 ? ccy - clusterW / 2 : ccy + clusterW / 2 - aS;
                    items.Add(new Item { R = new Rectangle(x, ay, aS, aS), E = realAddons[i] });
                }
                // la ofrenda va en la esquina libre de arriba a la derecha del rombo (se dibuja antes: si se toca con un perk, el perk queda encima)
                if (st.Offering != null && st.Offering.Rarity != "nota")
                {
                    // el hueco libre es el triangulo de la esquina con catetos D: el vertice de abajo a la izquierda
                    // del hexagono (0,88 S hacia adentro, 0,75 S hacia abajo) tiene que quedar fuera de los rombos
                    int S = (D - 6) * 100 / 163, w = S * 88 / 100;
                    items.Add(new Item { R = new Rectangle(x + clusterW - w, ccy - clusterW / 2, w, S), E = st.Offering });
                }
                var centers = new[] { new Point(ccx, ccy - o), new Point(ccx - o, ccy), new Point(ccx + o, ccy), new Point(ccx, ccy + o) };
                for (int i = 0; i < 4; i++)
                {
                    Entry pe = i < st.Perks.Count ? st.Perks[i] : null; bool gh = false;
                    if (pe == null && gi < ghosts.Count) { pe = ghosts[gi++]; gh = true; } // lugar libre: perk de la build sugerida
                    items.Add(new Item { R = new Rectangle(centers[i].X - D / 2, centers[i].Y - D / 2, D, D), E = pe, Slot = true, Ghost = gh });
                }
                blockW = x + clusterW - pad;
                y = top + blockH + (ShowBuildButtons ? 14 : 6); // lugar para los puntitos de las builds
            }
            int blockItems = items.Count, blockTexts = texts.Count;

            // addon dudoso: los candidatos numerados para elegir ("el dos")
            var pickItems = new List<Item>(); TextItem pickText = null; int rowW = 0;
            if (st.Pick != null && Environment.TickCount - st.PickUntil < 0)
            {
                for (int i = 0; i < st.Pick.Count; i++)
                    pickItems.Add(new Item { R = new Rectangle(i * (A + 8), y, A, A), E = st.Pick[i], Num = i + 1 });
                rowW = st.Pick.Count * (A + 8) - 8;
                string pt = L("¿cuál? ", "which one? ") + string.Join(" · ", st.Pick.Select((x, i) => (i + 1) + " " + DName(x)));
                var ps = M(pt, fSmall);
                int pw = Math.Min(ps.Width, 560);
                pickText = new TextItem { T = pt, F = fSmall, C = Color.FromArgb(240, 190, 60), P = new Point(0, y + A + 6), W = pw };
                rowW = Math.Max(rowW, pw);
                y += A + 6 + ps.Height + 6;
            }

            // todo alineado a la derecha del bloque mas ancho (killer, rombo, candidatos)
            int W = Math.Max(blockW, Math.Max(ktext != null ? ktext.W : 0, rowW));
            blockX = W - blockW;
            if (blockX > 0)
            {
                for (int i = 0; i < blockItems; i++) items[i].R.Offset(blockX, 0);
                for (int i = 0; i < blockTexts; i++) texts[i].P = new Point(texts[i].P.X + blockX, texts[i].P.Y);
            }
            if (ktext != null) { ktext.P = new Point(pad + W - ktext.W, ktext.P.Y); texts.Add(ktext); }
            int rowIconsW = pickItems.Count > 0 ? pickItems.Count * (A + 8) - 8 : 0;
            foreach (var pi in pickItems) { pi.R.Offset(pad + W - rowIconsW, 0); items.Add(pi); }
            if (pickText != null) { pickText.P = new Point(pad + W - pickText.W, pickText.P.Y); texts.Add(pickText); }
            blockW = W;

            var bottom = new List<Tuple<string, Color>>();
            if (talking) bottom.Add(Tuple.Create(L("● Escuchando…", "● Listening…"), Color.FromArgb(255, 80, 80)));
            else if (busy) bottom.Add(Tuple.Create("… Procesando", Color.Khaki));
            if (statusLine.Length > 0) bottom.Add(Tuple.Create(statusLine, Color.Orange));
            if (heardLine.Length > 0) bottom.Add(Tuple.Create(heardLine, Color.FromArgb(160, 160, 175)));
            if (feedback.Length > 0) bottom.Add(Tuple.Create(feedback, feedbackColor));
            // siempre a la vista: si estas en una sala (linea chica, se apaga desde Sala compartida)
            string rl = RoomLine();
            if (rl != null) bottom.Add(Tuple.Create(rl, roomStatus == Lang.L("conectado", "connected") || !ManualRoom ? Color.FromArgb(110, 190, 125) : Color.FromArgb(220, 170, 90)));
            if (!any && ktext == null && bottom.Count == 0 && !helpShown)
                if (TypeKeyText.Length > 0 || voice)
                    bottom.Add(Tuple.Create((voice ? KeyName + L(" hablar", " talk") + (TypeKeyText.Length > 0 ? " · " : "") : "") + (TypeKeyText.Length > 0 ? TypeKeyText + L(" anotar", " add") : ""), dim));

            int contentW = blockW;
            // ayuda (mientras se mantiene la tecla de ayuda)
            var helpItems = new List<TextItem>();
            if (helpShown)
            {
                if (any) y += 4;
                foreach (var hl in HelpLines())
                {
                    var f = hl.Item3 ? fItem : fSmall;
                    var sz = M(hl.Item1, f);
                    int w = Math.Min(sz.Width, 620);
                    helpItems.Add(new TextItem { T = hl.Item1, F = f, C = hl.Item2, P = new Point(pad, y), W = w });
                    y += sz.Height + (hl.Item3 ? 4 : 2);
                    contentW = Math.Max(contentW, w);
                }
                y += 6;
            }
            var bottomItems = new List<TextItem>();
            foreach (var b in bottom)
            {
                var sz = M(b.Item1, fSmall);
                int w = Math.Min(sz.Width, 560);
                bottomItems.Add(new TextItem { T = b.Item1, F = fSmall, C = b.Item2, P = new Point(pad, y), W = w });
                y += sz.Height + 2;
                contentW = Math.Max(contentW, w);
            }
            if (!any) y += 2;
            else if (bottom.Count == 0) y -= 6;

            var size = new Size(contentW + pad * 2, y + pad - 2);
            string pos = cfg.Get("Posicion", "arriba-derecha").ToLowerInvariant();
            bool right = !pos.Contains("izq");
            // pegado al borde: si aparece un mensaje largo, el rombo no se mueve
            if (right)
            {
                int dx = contentW - blockW;
                foreach (var it in items) it.R.Offset(dx, 0);
                foreach (var t in texts) t.P = new Point(t.P.X + dx, t.P.Y);
                foreach (var t in bottomItems) t.P = new Point(size.Width - pad - t.W, t.P.Y);
            }
            texts.AddRange(helpItems);
            texts.AddRange(bottomItems);
            var rt = bottomItems.FirstOrDefault(b => b.T.StartsWith("👥"));
            roomRect = rt == null ? Rectangle.Empty : new Rectangle(rt.P.X - 6, rt.P.Y - 4, rt.W + 12, M(rt.T, fSmall).Height + 8);

            var scr = Screen.PrimaryScreen.Bounds;
            int mx = cfg.GetInt("MargenX", 30), my = cfg.GetInt("MargenY", 140);
            int wx = right ? scr.Right - mx - size.Width : scr.Left + mx;
            int wy = pos.Contains("abajo") ? scr.Bottom - my - size.Height : scr.Top + my;
            var nb = new Rectangle(wx, wy, size.Width, size.Height);
            if (nb != Bounds && IsHandleCreated)
            {
                // moverse y redibujarse juntos: sin esto, por un instante se ve el dibujo viejo corrido (al aparecer un mensaje ancho)
                SetWindowPos(Handle, IntPtr.Zero, nb.X, nb.Y, nb.Width, nb.Height, 0x0004 | 0x0010 | 0x0100); // NOZORDER | NOACTIVATE | NOCOPYBITS
                if (Bounds != nb) Bounds = nb;
                if (typeBox != null && typeBox.Visible) PlaceTypeBox();
                Invalidate();
                if (Visible) Update();
                return;
            }
            Bounds = nb;
            if (typeBox != null && typeBox.Visible) PlaceTypeBox();
            Invalidate();
        }

        public static bool Preview;
        static readonly Color KeyColor = Color.FromArgb(1, 0, 1);       // color que Windows vuelve transparente (casi negro: bordes suavizados sin halo)
        static readonly Color PanelColor = Color.FromArgb(14, 14, 18);   // caja detras de los textos

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(KeyColor);
            // caja oscura solo donde hay texto (cada linea con margen; las lineas seguidas se juntan en un bloque)
            if (texts.Count > 0)
            {
                using (var br = new SolidBrush(PanelColor))
                using (var region = new Region(Rectangle.Empty))
                {
                    foreach (var t in texts)
                    {
                        var r = new Rectangle(t.P.X, t.P.Y, t.W + 2, t.F.Height + 2);
                        r.Inflate(6, 3);
                        region.Union(r);
                    }
                    g.FillRegion(br, region);
                }
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            {
                var rb = RotateButton();
                if (!rb.IsEmpty)
                {
                    // boton "siguiente build" con el estilo de los perks: triangulo oscuro, borde lila, ›› color hueso; con el cursor encima, borde rojo
                    var tri = new[] { new PointF(rb.Right, rb.Bottom), new PointF(rb.Right, rb.Y), new PointF(rb.X, rb.Bottom) };
                    using (var b = new SolidBrush(rotHover ? Color.FromArgb(62, 50, 62) : Color.FromArgb(48, 44, 60))) g.FillPolygon(b, tri);
                    var tin = new[] { new PointF(rb.Right - 4, rb.Bottom - 4), new PointF(rb.Right - 4, rb.Y + 9.7f), new PointF(rb.X + 9.7f, rb.Bottom - 4) };
                    using (var p = new Pen(Color.FromArgb(70, 120, 110, 140), 1f)) g.DrawPolygon(p, tin);   // filete interior, como el marco de los perks
                    using (var p = new Pen(rotHover ? Color.FromArgb(200, 40, 50) : Color.FromArgb(120, 110, 140), rotHover ? 2f : 1.4f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round }) g.DrawPolygon(p, tri);
                    // ›› en el centro del triangulo
                    // centro: el punto a igual distancia de los tres lados (en un triangulo rectangulo isosceles, 0,293 del cateto)
                    float gx = rb.Right - rb.Width * 0.2929f, gy = rb.Bottom - rb.Height * 0.2929f, cs = rb.Width * 0.12f;
                    using (var p = new Pen(rotHover ? Color.White : Color.FromArgb(214, 204, 186), Math.Max(2f, rb.Width / 20f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                        for (int c2 = 0; c2 < 2; c2++)
                        {
                            float ox = gx - cs * 0.6f + c2 * cs * 1.2f;   // los dos chevrones juntos quedan centrados en gx
                            g.DrawLines(p, new[] { new PointF(ox - cs * 0.5f, gy - cs), new PointF(ox + cs * 0.5f, gy), new PointF(ox - cs * 0.5f, gy + cs) });
                        }
                }
                var dots = BuildDots();
                int cb = buildsHidden ? dots.Count - 1 : CurBuild();
                for (int k = 0; k < dots.Count; k++)
                {
                    bool on = k == cb, hideDot = k == dots.Count - 1;
                    Color dc = on ? (hideDot ? Color.FromArgb(220, 70, 80) : Color.FromArgb(150, 200, 255)) : Color.FromArgb(80, 85, 100);
                    if (hideDot)
                        using (var p = new Pen(dc, 1.6f)) { g.DrawLine(p, dots[k].X - 2.5f, dots[k].Y - 2.5f, dots[k].X + 2.5f, dots[k].Y + 2.5f); g.DrawLine(p, dots[k].X - 2.5f, dots[k].Y + 2.5f, dots[k].X + 2.5f, dots[k].Y - 2.5f); }
                    else using (var b = new SolidBrush(dc)) g.FillPolygon(b, new[] { new PointF(dots[k].X, dots[k].Y - 3.5f), new PointF(dots[k].X + 3.5f, dots[k].Y), new PointF(dots[k].X, dots[k].Y + 3.5f), new PointF(dots[k].X - 3.5f, dots[k].Y) }); // rombitos
                }
            }
            foreach (var it in items)
            {
                if (it.E != null) DrawIcon(g, it.E, it.R);
                if (it.Ghost)
                {
                    var r = it.R;
                    var dp = new[] { new Point(r.X + r.Width / 2, r.Y), new Point(r.Right, r.Y + r.Height / 2), new Point(r.X + r.Width / 2, r.Bottom), new Point(r.X, r.Y + r.Height / 2) };
                    using (var b = new SolidBrush(Color.FromArgb(150, 14, 14, 18))) g.FillPolygon(b, dp);
                    using (var p = new Pen(Color.FromArgb(200, 120, 180, 240), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawPolygon(p, dp);
                }
                if (it.E != null && it.E == holdE && holdShown)
                {
                    var r = it.R;
                    var shape = it.Slot
                        ? new[] { new PointF(r.X + r.Width / 2f, r.Y), new PointF(r.Right, r.Y + r.Height / 2f), new PointF(r.X + r.Width / 2f, r.Bottom), new PointF(r.X, r.Y + r.Height / 2f) }
                        : new[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) };
                    using (var b = new SolidBrush(Color.FromArgb(90, 120, 20, 28))) g.FillPolygon(b, shape);   // oscurecido
                    var filled = BelowLine(shape, r.Bottom - r.Height * holdP);                                   // lo que ya se lleno (de abajo hacia arriba)
                    if (filled.Length >= 3) using (var b = new SolidBrush(Color.FromArgb(185, 215, 30, 42))) g.FillPolygon(b, filled);
                    using (var p = new Pen(Color.FromArgb(230, 60, 70), 2f)) g.DrawPolygon(p, shape);
                    using (var f = new Font("Segoe UI", Math.Max(10, r.Width / 3), FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, "✖", f, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (it.Num > 0)
                {
                    // numero del candidato (abajo a la izquierda)
                    int bs = Math.Max(16, it.R.Width * 2 / 5);
                    var br = new Rectangle(it.R.X - 3, it.R.Bottom - bs + 3, bs, bs);
                    using (var b = new SolidBrush(Color.FromArgb(240, 240, 245))) g.FillEllipse(b, br);
                    using (var f = new Font("Segoe UI", bs * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, it.Num.ToString(), f, br, Color.FromArgb(20, 20, 28),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (it.E != null) { }
                else if (it.Slot)
                {
                    var r = it.R;
                    var pts = new[] { new Point(r.X + r.Width / 2, r.Y + 3), new Point(r.Right - 3, r.Y + r.Height / 2),
                                      new Point(r.X + r.Width / 2, r.Bottom - 3), new Point(r.X + 3, r.Y + r.Height / 2) };
                    using (var pen = new Pen(Color.FromArgb(70, 70, 82))) g.DrawPolygon(pen, pts);
                }
            }
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            foreach (var t in texts)
                TextRenderer.DrawText(g, t.T, t.F, new Rectangle(t.P.X, t.P.Y, t.W + 2, t.F.Height + 2), t.C, t.Center ? flags | TextFormatFlags.HorizontalCenter : flags);
        }

        void DrawIcon(Graphics g, Entry e, Rectangle r)
        {
            if (e.K == Kind.Perk)
            {
                // rombo como en el juego
                var pts = new[] { new Point(r.X + r.Width / 2, r.Y), new Point(r.Right, r.Y + r.Height / 2),
                                  new Point(r.X + r.Width / 2, r.Bottom), new Point(r.X, r.Y + r.Height / 2) };
                using (var br = new SolidBrush(Color.FromArgb(48, 44, 60))) g.FillPolygon(br, pts);
                using (var pen = new Pen(Color.FromArgb(120, 110, 140))) g.DrawPolygon(pen, pts);
            }
            else if (e.Rarity == "nota")
            {
                using (var pen = new Pen(Color.FromArgb(110, 110, 120)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(pen, r.X + 2, r.Y + 2, r.Width - 5, r.Height - 5);
                TextRenderer.DrawText(g, "?", fItem, r, Color.FromArgb(150, 150, 160),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            else if (e.K == Kind.Offering && e.Rarity != "secreta")
            {
                // ofrenda como en el juego: hexagono con punta arriba y abajo, del color de la rareza
                var c = RarityColor(e.Rarity);
                int w = r.Height * 88 / 100, cx = r.X + r.Width / 2, q = r.Height / 4;
                r = new Rectangle(cx - w / 2, r.Y, w, r.Height);
                var hex = new[] { new Point(cx, r.Y), new Point(r.Right, r.Y + q), new Point(r.Right, r.Bottom - q),
                                  new Point(cx, r.Bottom), new Point(r.X, r.Bottom - q), new Point(r.X, r.Y + q) };
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(r, Color.FromArgb(c.R * 2 / 3, c.G * 2 / 3, c.B * 2 / 3), Color.FromArgb(c.R / 4, c.G / 4, c.B / 4), 90f))
                    g.FillPolygon(br, hex);
                using (var pen = new Pen(Color.FromArgb(c.R * 4 / 5 + 20, c.G * 4 / 5 + 20, c.B * 4 / 5 + 20), Math.Max(1.5f, r.Height / 28f)))
                    g.DrawPolygon(pen, hex);
                r.Inflate(-r.Width / 7, -r.Height / 7); // el dibujo dentro del hexagono
            }
            else
            {
                // fondo del color de la rareza, como en el juego
                // (la ofrenda secreta es el dorso de la carta: ya tiene su forma, va sin fondo ni borde)
                if (e.Rarity != "secreta")
                {
                    var c = RarityColor(e.Rarity);
                    using (var br = new SolidBrush(Color.FromArgb(Math.Max(0, c.R / 2), Math.Max(0, c.G / 2), Math.Max(0, c.B / 2))))
                        g.FillRectangle(br, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
                    // la ofrenda lleva borde dorado para distinguirla de los addons
                    if (e.K == Kind.Offering)
                        using (var pen = new Pen(Color.FromArgb(215, 180, 95), 2)) g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                }
            }
            bool doubt = st.IsDoubt(e);
            var img = GetIcon(e.Icon);
            if (img != null)
            {
                var ir = r;
                System.Drawing.Drawing2D.GraphicsPath clip = null;
                var oldClip = g.Clip;
                if (e.K == Kind.Perk)
                {
                    // el dibujo de la wiki ya trae margen: casi sin achicar, recortado al rombo (sin pisar el borde)
                    ir.Inflate(-r.Width / 32, -r.Height / 32);
                    clip = new System.Drawing.Drawing2D.GraphicsPath();
                    clip.AddPolygon(new[] { new Point(r.X + r.Width / 2, r.Y + 2), new Point(r.Right - 2, r.Y + r.Height / 2),
                                            new Point(r.X + r.Width / 2, r.Bottom - 2), new Point(r.X + 2, r.Y + r.Height / 2) });
                    g.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                }
                if (!doubt) g.DrawImage(img, ir);
                else
                {
                    // sospecha: el icono a medias
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.4f };
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        g.DrawImage(img, ir, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                if (clip != null) { g.Clip = oldClip; clip.Dispose(); }
            }
            else if (e.Rarity == "mapa")
                // ofrenda de mapa (cualquiera): sin dibujo propio, dice MAPA
                using (var f = new Font("Bahnschrift SemiBold SemiConden", Math.Max(7f, r.Height * 0.3f), GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, L("MAPA", "MAP"), f, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (doubt)
            {
                // "?" amarillo en la esquina
                int bs = Math.Max(14, r.Width * 2 / 5);
                var br = new Rectangle(r.Right - bs + (e.K == Kind.Perk ? -bs / 6 : 2), r.Y + (e.K == Kind.Perk ? bs / 6 : -2), bs, bs);
                using (var b = new SolidBrush(Color.FromArgb(240, 190, 60))) g.FillEllipse(b, br);
                using (var f = new Font("Segoe UI", bs * 0.48f, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "?", f, br, Color.FromArgb(30, 24, 10),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        bool restartingForUpdate;
        public void RestartApp()
        {
            FlushCfg();
            restartingForUpdate = true;
            try { Process.Start(Application.ExecutablePath, "--reinicio " + Process.GetCurrentProcess().Id); } catch { }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopKeyHook();
            // cerrar la app = salir de la sala con codigo (avisa a los demas y libera el lugar).
            // Si se reinicia por una actualizacion, sigue adentro.
            if (ManualRoom && !restartingForUpdate)
            {
                if (room != null) Room.PublishNow(room.Topic, Stamp(new Dictionary<string, object> { { "leave", true }, { "t", UnixNow() }, { "from", room.Me } }), 4000);
                SetCfg("Sala", "");
                DebugEvent("sala: salí al cerrar la app");
            }
            st.Save(Path.Combine(dir, "estado.txt"));
            if (mic != null) mic.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Lang.FirstRun = !File.Exists(Path.Combine(dir, "config.ini")); // antes de que Cfg.Load lo cree
            try { Lang.Init(Cfg.Load(Path.Combine(dir, "config.ini")).Get("IdiomaApp", "auto")); } catch { }
            // un error en la ventana: queda anotado en logsrrores.log (con el detalle) y la app sigue, sin el cartel de .NET
            var seenErr = new HashSet<string>();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try
                {
                    string key = e.Exception.GetType().Name + e.Exception.StackTrace;
                    if (!seenErr.Add(key)) return; // el mismo error al repintar: una vez alcanza
                    string folder = Path.Combine(dir, "logs");
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "errores.log"), "---- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " v" + Updater.AppVersion + "\r\n" + e.Exception + "\r\n\r\n", Encoding.UTF8);
                }
                catch { }
            };
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 16; // la sala tiene una conexion abierta escuchando todo el tiempo
            if (args.Length >= 3 && args[0] == "--desc")
            {
                // --desc "The Huntress" salida.txt : palabras de cada addon (depuracion)
                var cat = Catalog.Load(Path.Combine(dir, "datos"));
                var sb = new StringBuilder();
                foreach (var a in cat.Addons.Where(x => x.Killer == args[1]))
                    sb.Append(a.Name + " [" + a.Icon + "]: " + string.Join(" ", a.Desc) + "\r\n    ES: " + (a.Info ?? "").Replace("\n", " / ") +
                              "\r\n    EN: " + (a.InfoEn ?? "").Replace("\n", " / ") + "\r\n");
                if (args.Length >= 4) sb.Append("TOKENS: " + string.Join(" ", Txt.Tokens(args[3])) + "\r\n");
                File.WriteAllText(args[2], sb.ToString(), Encoding.UTF8);
                return;
            }
            if (args.Length >= 3 && args[0] == "--ocr-mapa")
            {
                // --ocr-mapa salida.txt captura1.png [captura2.png ...] : lee el nombre del mapa de capturas de pantalla completas
                var cat = Catalog.Load(Path.Combine(dir, "datos"));
                var sb = new StringBuilder();
                foreach (var f in args.Skip(2))
                {
                    var sw = Stopwatch.StartNew();
                    using (var src = new Bitmap(f))
                    {
                        var all = new Rectangle(0, 0, src.Width, src.Height);
                        using (var crop = src.Clone(MapReader.Region(all), src.PixelFormat))
                        using (var big = MapReader.Prepare(crop))
                        {
                            var lines = WinOcr.Read(big); string why;
                            var name = MapReader.Match(cat, lines, out why);
                            sb.Append(Path.GetFileName(f) + " (" + sw.ElapsedMilliseconds + " ms)\r\n  mapa: [" + (lines == null ? "SIN OCR" : string.Join(" | ", lines)) + "] -> " + (name ?? "NADA") + "   (" + why + ")\r\n");
                        }
                        using (var crop = src.Clone(MapReader.StatusRegion(all), src.PixelFormat))
                        using (var big = MapReader.Prepare(crop))
                        using (var con = MapReader.Contrast(crop))
                        {
                            var lines = (WinOcr.Read(big) ?? new string[0]).Concat(WinOcr.Read(con) ?? new string[0]).ToList(); string why;
                            var found = MapReader.MatchSources(cat, lines, null, out why);
                            sb.Append("  estados: [" + string.Join(" | ", lines) + "] -> " + why + "\r\n");
                        }
                    }
                }
                File.WriteAllText(args[1], sb.ToString(), Encoding.UTF8);
                return;
            }
            if (args.Length >= 3 && args[0] == "--escuchar")
            {
                // --escuchar segundos salida.txt : prueba de transcribir lo que suena en la PC (Discord); no toca la lista
                ListenTest.Run(dir, int.Parse(args[1]), args[2]);
                return;
            }
            if (args.Length >= 2 && args[0] == "--ofrendas")
            {
                // --ofrendas salida.txt : lista de ofrendas con su icono (depuracion)
                var cat = Catalog.Load(Path.Combine(dir, "datos"));
                File.WriteAllLines(args[1], cat.Offerings.Select(o => o.Name + " | " + o.Rarity + " | " + (o.Icon ?? "SIN ICONO") + " | " + string.Join(" ", o.Desc.Take(12))), Encoding.UTF8);
                return;
            }
            if (args.Length >= 2 && args[0] == "--icono")
            {
                // --icono app.ico [muestra.png] : genera el icono del .exe (y una hoja con los tamaños para mirarlo)
                Overlay.WriteIco(args[1]);
                if (args.Length >= 3) { int[] ss = { 16, 24, 32, 48, 64, 128, 256 }; var sheet = new Bitmap(ss.Sum() + 10 * ss.Length + 10, 276); using (var g = Graphics.FromImage(sheet)) { g.Clear(Color.FromArgb(40, 40, 46)); int x = 10; foreach (var s in ss) { using (var b = Overlay.Logo(s, Color.FromArgb(190, 22, 34))) g.DrawImage(b, x, 10); if (s <= 64) using (var b = Overlay.Logo(s, Color.FromArgb(40, 150, 70))) g.DrawImage(b, x, 200); x += s + 10; } } sheet.Save(args[2]); }
                return;
            }
            if (args.Length >= 2 && args[0] == "--steam-watch")
            {
                SteamDbd.Watch(int.Parse(args[1]));
                return;
            }
            if (args.Length >= 2 && args[0] == "--steam")
            {
                // --steam salida.txt : solo con DBD abierto (Steam cuenta este proceso como parte del juego)
                string res;
                if (!SteamDbd.GameRunning() && !(args.Length >= 3 && args[2] == "--forzar")) res = "DBD no está abierto: no pruebo (Steam te mostraría jugando DBD)\r\n";
                else { try { res = SteamDbd.Probe(); } catch (Exception ex) { res = "ERROR: " + ex + "\r\n"; } }
                File.WriteAllText(args[1], res, Encoding.UTF8);
                return;
            }
            if (args.Length >= 4 && args[0] == "--sala")
            {
                // --sala codigo salida.txt "frase" ... : dos PCs simuladas (A habla, B solo ve) y una C que entra despues
                var cat = Catalog.Load(Path.Combine(dir, "datos"));
                var sb = new StringBuilder();
                var lk = new object();
                Func<string, State, Room> Join = (who, s) =>
                {
                    var r = new Room();
                    r.OnOp = op => { lock (lk) { string w = s.ApplyRemote(op); sb.Append(who + " recibe de " + op["name"] + ": " + (w.Length > 0 ? w : "(nada nuevo)") + "\r\n"); } };
                    r.OnStatus = x => { lock (lk) sb.Append(who + " estado: " + x + "\r\n"); };
                    r.Start(Room.TopicFor(args[1]));
                    return r;
                };
                Func<State, string> D = s => (s.Killer != null ? s.Killer.Name : "-") + " | " + string.Join(", ", s.Perks.Select(p => p.Name)) + " | " + string.Join(", ", s.Addons.Select(a => a.Name));
                var sa = new State(cat); var sbb = new State(cat);
                var ra = Join("A", sa); var rb = Join("B", sbb);
                Thread.Sleep(3000);
                foreach (var f in args.Skip(3))
                {
                    var before = sa.Snapshot();
                    string res;
                    if (f == "RESET") { sa.Clear(); res = "reset"; ra.Publish(new Dictionary<string, object> { { "reset", true }, { "name", "A" } }); }
                    else
                    {
                        lock (lk) res = sa.Apply(f);
                        var op = sa.Diff(before);
                        if (op != null) { op["name"] = "A"; ra.Publish(op); }
                    }
                    lock (lk) sb.Append("A dice \"" + f + "\" => " + res + "\r\n");
                    Thread.Sleep(1500);
                }
                Thread.Sleep(3000);
                var sc = new State(cat); var rc = Join("C", sc);
                Thread.Sleep(5000);
                lock (lk)
                {
                    sb.Append("A: " + D(sa) + "\r\nB: " + D(sbb) + "\r\nC: " + D(sc) + "\r\n");
                    sb.Append(D(sa) == D(sbb) && D(sa) == D(sc) ? "OK: iguales\r\n" : "DISTINTOS\r\n");
                }
                ra.Stop(); rb.Stop(); rc.Stop();
                File.WriteAllText(args[2], sb.ToString(), Encoding.UTF8);
                return;
            }
            if (args.Length >= 2 && args[0] == "--test")
            {
                // --test entrada.txt : cada linea "frase" (o "frase -> esperado") y escribe entrada.txt.out
                var cat = Catalog.Load(Path.Combine(dir, "datos"));
                var st = new State(cat) { AliasPath = args.Length >= 3 ? args[2] : Path.Combine(dir, "apodos.txt") };
                st.ReloadAliases();
                var sb = new StringBuilder(cat.Info + "\r\n");
                foreach (var line in File.ReadAllLines(args[1], Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) continue;
                    st.Trace.Clear();
                    string res = st.Apply(line);
                    sb.Append(line.PadRight(45) + " => " + res + "\r\n");
                    foreach (var tr in st.Trace) sb.Append("        " + tr + "\r\n");
                }
                sb.Append("ESTADO: " + (st.Killer != null ? st.Killer.Name : "-") + " | " + string.Join(", ", st.Perks.Select(p => p.Name)) + " | " + string.Join(", ", st.Addons.Select(a => a.Name)) + "\r\n");
                File.WriteAllText(args[1] + ".out", sb.ToString(), Encoding.UTF8);
                return;
            }
            if (args.Length >= 2 && args[0] == "--gifs")
            {
                // --gifs carpeta : cuadros de los GIFs de la guia (en una copia con config propia, ver MakeGifs)
                Overlay.Preview = true; Overlay.GifOut = args[1];
                Application.EnableVisualStyles();
                Application.Run(new Overlay(dir));
                return;
            }
            if (args.Length >= 1 && args[0] == "--vista")
            {
                // copia de prueba en otra carpeta: se puede abrir con el overlay real andando
                Overlay.Preview = true;
                Application.EnableVisualStyles();
                Application.Run(new Overlay(dir));
                return;
            }
            if (args.Length >= 2 && args[0] == "--reinicio")
            {
                // despues de actualizarse: esperar a que cierre la version vieja (libera el bloqueo de instancia unica)
                try { var old = Process.GetProcessById(int.Parse(args[1])); old.WaitForExit(15000); } catch { }
            }
            if (args.Length >= 2 && args[0] == "--actualizar")
            {
                // prueba/diagnostico: buscar e instalar la version nueva sin abrir el overlay
                string err, what = Updater.CheckAndInstall(out err);
                File.WriteAllText(args[1], "local " + Updater.AppVersion + " | instalada: " + (what ?? "-") + " | error: " + (err ?? "-"));
                return;
            }
            if (args.Length >= 1 && args[0] == "--version") { File.WriteAllText(Path.Combine(dir, "version.txt"), Updater.AppVersion.ToString()); return; }
            bool created;
            // --prueba: una copia de prueba en otra carpeta puede correr junto a la de verdad (para probar salas)
            string mxName = args.Length >= 1 && args[0] == "--prueba" ? "DbdOverlay_Prueba_" + (dir.GetHashCode() & 0x7fffffff) : "DbdOverlay_SingleInstance";
            using (var mx = new Mutex(true, mxName, out created))
            {
                if (!created) { MessageBox.Show(Lang.L("Callouts in the Fog ya está abierto (mirá el ícono del gancho en la barra de tareas).", "Callouts in the Fog is already running (look for the hook icon in the taskbar)."), Overlay.AppName); return; }
                Application.EnableVisualStyles();
                Application.Run(new Overlay(dir));
            }
        }
    }
}
