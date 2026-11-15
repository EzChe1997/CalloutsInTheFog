// DBD Overlay - anotar por voz los perks y addons del killer.
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
@"# DBD Overlay - configuracion (se lee al abrir el programa)

# Tecla que hay que MANTENER apretada mientras hablas.
# Ejemplos: Alt, AltIzq, AltDer, F8, V, XButton1 (boton lateral de atras del mouse), XButton2, Capital (Bloq Mayus)
Tecla=Alt

# Tecla para borrar todo: hay que MANTENERLA 1 segundo. Despues, un toque rapido la deshace (por unos segundos).
# (Por voz no se puede borrar todo, para que Whisper no lo haga sin querer.)
TeclaLimpiar=G

# Tecla para ver la ayuda con los comandos (mientras se mantiene apretada)
TeclaAyuda=H

# Microfono: vacio = el predeterminado de Windows. Si no, el principio del nombre como lo muestra Windows,
# por ejemplo: Microfono=Micrófono (NVIDIA Broadcast)
Microfono=

# Mostrar los nombres de perks/addons al lado del rombo (si/no). Tambien desde el menu del icono o diciendo ""mostrar nombres""
Nombres=no
# Tecla opcional para mostrar/ocultar nombres (vacio = ninguna)
TeclaNombres=
# Tamano de cada perk en pixeles
TamanoPerk=48

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
            s = Regex.Replace(s, @"^(T_)?icons?(Perks|Addon|Favors)_|\.png$", "", RegexOptions.IgnoreCase);
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
        public string Icon;                                  // archivo en deadbydaylight.wiki.gg/images/
        public HashSet<string> Desc = new HashSet<string>(); // palabras de la descripcion en espanol
        public string NameEs, Info, InfoEn;                  // nombre en espanol y descripciones (espanol / ingles) para leer (hover)
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

        static readonly string[][] PerkAliases = {
            new[]{"barbecue","bbq","barbacoa"}, new[]{"pop goes the weasel","pop","pgtw"},
            new[]{"no one escapes death","noed","no ed"}, new[]{"dead mans switch","dms"},
            new[]{"save the best for last","stbfl"}, new[]{"play with your food","pwyf"},
            new[]{"make your choice","myc"}, new[]{"surge","jolt","search","surch","serge"}, new[]{"thanatophobia","thana"},
            new[]{"monitor and abuse","monitor"}, new[]{"pain resonance","painless","pain res","pain reso"}
        };

        // ---- datos al dia: al abrir se mira /api/versions (menos de 1 KB) y solo se baja lo que cambio desde la ultima vez
        static readonly string[] Datasets = { "characters", "perks", "addons", "offerings" };
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
                DataNews = "datos del juego actualizados" + (GameVersion.Length > 0 ? " (" + GameVersion + ")" : "");
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
                    string cont = "";
                    for (int guard = 0; guard < 20; guard++)
                    {
                        string url = "https://deadbydaylight.wiki.gg/api.php?action=query&list=allimages&aiprefix=" + prefix +
                                     "&ailimit=500&format=json" + (cont.Length > 0 ? "&aicontinue=" + Uri.EscapeDataString(cont) : "");
                        var d = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(HttpBytes(url)));
                        var q = (Dictionary<string, object>)d["query"];
                        foreach (Dictionary<string, object> i in (System.Collections.ArrayList)q["allimages"]) names.Add(i["name"].ToString());
                        object c;
                        if (d.TryGetValue("continue", out c)) cont = ((Dictionary<string, object>)c)["aicontinue"].ToString();
                        else break;
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
            { "haste", "Prisa" }, { "hindered", "Obstaculizado" }, { "exposed", "Expuesto" }, { "exhausted", "Agotado" },
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
            var perkIcons = KeyMap(WikiList(dir, "wiki_perks.txt", "IconPerks_"));
            var addonIcons = KeyMap(WikiList(dir, "wiki_addons.txt", "IconAddon_"));

            var c = new Catalog();
            var byItem = new Dictionary<string, string>();
            foreach (var kv in chars)
            {
                var d = kv.Value as Dictionary<string, object>;
                if (d == null) continue;
                string name = WebUtility.HtmlDecode(S(d, "name"));
                var e = new Entry { K = Kind.Killer, Name = name };
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
                e.AddKey(name);
                int ci = name.IndexOf(':');
                if (ci > 0) e.AddKey(name.Substring(ci + 1));
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
                var offIcons = KeyMap(WikiList(dir, "wiki_ofrendas.txt", "IconFavors_"));
                foreach (var kv in offs)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null || S(d, "role") == "survivor" || S(d, "retired") == "1") continue;
                    string name = WebUtility.HtmlDecode(S(d, "name"));
                    if (name.Length == 0 || name.StartsWith("@#")) continue;
                    name = Regex.Replace(name, @"\b([A-Z])([A-Z]+)\b", m => m.Groups[1].Value + m.Groups[2].Value.ToLowerInvariant()); // "TERRORMISU" -> "Terrormisu"
                    var e = new Entry { K = Kind.Offering, Name = name, Killer = "", Rarity = S(d, "rarity") };
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

            c.Info = c.Killers.Count + " killers, " + c.Perks.Count + " perks, " + c.Addons.Count + " addons, " + c.Offerings.Count + " ofrendas, iconos: " +
                     c.Perks.Count(x => x.Icon != null) + " perks / " + c.Addons.Count(x => x.Icon != null) + " addons / " + c.Offerings.Count(x => x.Icon != null) + " ofrendas";
            c.Info += " · datos " + (GameVersion.Length > 0 ? GameVersion : "?") + (remoteVer == null ? " (sin conexión: copia guardada)" : DataNews.Length > 0 ? " (recién actualizados)" : " (al día)");
            SaveVersions(dir);
            return c;
        }
    }

    // ------------------------------------------------------------ estado + interpretacion
    class State
    {
        public Entry Killer;
        public List<Entry> Perks = new List<Entry>(), Addons = new List<Entry>();
        Catalog cat;
        public const double Threshold = 0.66;

        public State(Catalog c) { cat = c; }
        public string ClearKeyName = "G";

        // sospechas: "creo que tiene X" (se ven semitransparentes con "?"), en orden de cuando se dijeron
        public List<Entry> Doubt = new List<Entry>();
        // addon descripto de forma vaga: candidatos para elegir por numero (el primero queda anotado como sospecha)
        public List<Entry> Pick; public Entry PickEntry; public int PickUntil;
        static readonly Regex PickRx = new Regex(@"^(?:(?:es|era|seria)\s+)?(?:(?:el|la)\s+)?(?:numero\s+|opcion\s+)?(uno|1|primero|primer|primera|dos|2|segundo|segunda|tres|3|tercero|tercer|tercera|ninguno|ninguna|ningun)\b(?:\s+(?:es|era))?$");
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
            if (op.ContainsKey("reset")) { Clear(); done.Add("borró la lista"); }
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
                    done.Add("+ " + (e.K == Kind.Offering ? "ofrenda: " : "") + e.Name + (d ? " (?)" : ""));
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

        string AddOne(Entry e)
        {
            if (e.K == Kind.Killer) { Killer = e; return "killer: " + e.Name; }
            if (e.K == Kind.Offering)
            {
                if (Offering == e || (Offering != null && Offering.Name == e.Name))
                {
                    if (!doubtMode && Doubt.Remove(Offering)) return "✓ ofrenda " + Offering.Name + " confirmada";
                    return "la ofrenda " + e.Name + " ya estaba";
                }
                string prev = Offering != null ? " (antes " + Offering.Name + ")" : "";
                if (Offering != null) Doubt.Remove(Offering);
                Offering = e;
                if (doubtMode) { Doubt.Add(e); return "+ ofrenda: " + e.Name + " (?)" + prev; }
                return "+ ofrenda: " + e.Name + prev;
            }
            var list = e.K == Kind.Perk ? Perks : Addons;
            int max = e.K == Kind.Perk ? 4 : 2;
            if (list.Contains(e))
            {
                // "tiene X" con X en duda = lo confirma
                if (!doubtMode && Doubt.Remove(e)) return "✓ " + e.Name + " confirmado";
                return e.Name + " ya estaba";
            }
            if (list.Count >= max) return "ya tiene " + max + (max == 4 ? " perks" : " addons") + ", no anoté " + e.Name + " (decí \"quitar ...\")";
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
                if (e != null) { Doubt.Remove(e); return "✓ " + e.Name + " confirmado"; }
                if (Strip(rest).Length == 0) return "no hay nada en duda para confirmar";
                t = "tiene " + rest; // "confirmado X" con X que no estaba en duda: se anota seguro
                return null;
            }
            var dm = DenyRx.Match(t);
            if (dm.Success)
            {
                string rest = dm.Groups[1].Value;
                T("modo: descartar -> \"" + rest + "\" (sospechas: " + Doubt.Count + ")");
                var e = PickDoubt(rest);
                if (e != null) { RemoveEntry(e); return "✗ " + e.Name + " descartado"; }
                if (Strip(rest).Length == 0) return "no hay nada en duda para descartar";
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

        public string Apply(string raw)
        {
            string t0 = Txt.Norm(raw);
            // "no" a secas: descarta la sugerencia (¿...?) o, si no habia, deshace lo ultimo
            bool bareNo = Regex.IsMatch(t0, @"^(no|no no|nop|nah|negativo)$");
            if (bareNo && lastGuess != null)
            {
                lastGuess = null; lastFail = null;
                return "ok, no lo anoto";
            }
            if (bareNo || UndoRx.IsMatch(t0))
            {
                if (undoSnap == null) return "no hay nada para deshacer";
                Restore(undoSnap);
                if (undoAlias != null) RemoveAlias(undoAlias);
                exclude = new HashSet<Entry>(undoAdded.Where(x => x.Rarity != "nota"));
                string what = undoAdded.Count > 0 ? string.Join(", ", undoAdded.Select(x => x.Name)) : "el último cambio";
                undoSnap = null; undoAdded = new List<Entry>(); undoAlias = null;
                return "deshice: " + what + (exclude.Count > 0 ? " · repetilo, no lo voy a elegir de nuevo" : "");
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
                undoSnap = before; undoAdded = added; undoAlias = learnedAlias;
            }
            return res;
        }

        // devuelve la descripcion de lo que hizo
        string ApplyInner(string raw)
        {
            string t = Txt.Norm(raw);
            if (t.Length == 0) return "no escuché nada";
            // borrar todo NO se hace por voz (Whisper podria entenderlo sin querer): se usa la tecla
            if (Regex.IsMatch(t, @"\b(nueva partida|otra partida|borra(r)? todo|limpia(r)?( todo)?|reinicia(r)?|reset)\b"))
                return "no borro por voz: mantené " + ClearKeyName + " 1 segundo";
            ReloadAliases();

            // muletillas al principio: "ah ...", "anotá (que) ...", "poneme ..."
            t = Regex.Replace(t, @"^(?:(?:ah|eh|bueno|ok|anota(?:me|le)?|anata|anote|pone(?:me|le)?|agrega(?:me|le)?)\s+(?:(?:de|que)\s+)?)+", "");
            // como escribe Whisper "el killer es": "Killers: Killers ...", "killeresk", "alquileres", "el que eres"
            // (tambien "kirer es", "kiler es"; "killer" solo no se toca: "killer X" ya se entiende)
            t = Regex.Replace(t, @"^(?:(?:el|al)\s+)?(?:al|el)?(?:k|qu)i(?:ll?|r)er(?:e?s|esk|\s+es)\b(?:\s+(?:k|qu)i(?:ll?|r)er(?:e?s|esk|\s+es)?\b)*", "el killer es");
            t = Regex.Replace(t, @"^el que eres?\b", "el killer es");
            if (t.Length == 0) return "no entendí";

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
                    if (w.StartsWith("ningun")) { RemoveEntry(old); return "- addon " + old.Name + " (ninguno de esos: describilo de otra forma)"; }
                    int idx = w == "uno" || w == "1" || w.StartsWith("primer") ? 0 : w == "dos" || w == "2" || w.StartsWith("segund") ? 1 : 2;
                    if (idx >= cands.Count) return "solo había " + cands.Count + " opciones";
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
                return AddOne(guess) + " · aprendí \"" + fail + "\"";
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
                if (nick.Length == 0 || h == null || h.S < Threshold) return "no entendí a qué te referís con \"" + target + "\"";
                AddAlias(nick, h.E);
                learnedAlias = Txt.Norm(nick);
                return "aprendí: \"" + nick + "\" = " + h.E.Name;
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
                if (Addons.Count == 0) return "no hay addons para quitar";
                Addons.Clear();
                return "addons borrados";
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
                    if (Offering == null) return "no hay ofrenda para quitar";
                    string n = Offering.Name; RemoveEntry(Offering);
                    return "- ofrenda " + n;
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
            if (t.Length == 0) return "no entendí";

            var pool = remove ? Items().ToList() : perkOnly ? cat.Perks.ToList() : AddPool(killerOnly).ToList();
            if (pool.Count == 0) return "no hay nada para quitar";

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
                    if (whole == null || whole.S < 0.4) return "no entendí";
                    // no ofrecer "¿X?" (ni aprender con "sí") si la frase era un comando que no se entendió
                    if (!remove && !Regex.IsMatch(t, @"\b(apodo|alias|killers?|asesino|anota|ofrenda)\b")) { lastGuess = whole.E; lastFail = t; }
                    else return "no entendí";
                    return "no entendí (¿" + whole.E.Name + "?)" + (remove ? "" : " — decí \"sí\" si era eso");
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
            if (n.Length == 0) return "¿qué ofrenda? (decí \"ofrenda\" y el nombre o lo que hace)";
            // 1) el nombre (ingles o espanol, o apodo como "mori")
            var best = Best(n, cat.Offerings);
            if (best != null && best.S >= 0.75) return AddOne(best.E);
            // 2) por lo que hace / el mapa ("de mapa de Haddonfield", "que pone mas niebla")
            List<Entry> ties;
            var e = MatchDesc(text, cat.Offerings, out ties);
            if (e != null) return AddOne(e);
            string extra = ties.Count > 0 ? " (puede ser " + string.Join(" o ", ties.Take(3).Select(x => x.Name)) + ")" : " (no la reconocí)";
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

        string AddAddonText(string text)
        {
            text = Regex.Replace(text, @"^\W*(?:(?:tienes?|lleva|usa|tambien|también)\b[\s.,:;…]*)+", "", RegexOptions.IgnoreCase).Trim();
            string n = Strip(Txt.Norm(text));
            if (n.Length == 0) return "no entendí qué hace el addon";
            var pool = KillerAddons();
            // 1) dijo el nombre real (en ingles o en espanol)
            var best = Best(n, pool);
            Entry e = (best != null && best.S >= (Killer != null ? 0.78 : 0.85)) ? best.E : null;
            // 1b) en realidad es un perk ("Monstrous Shrine" transcripto como addon)
            if (e == null)
            {
                var bp = Best(n, cat.Perks);
                if (bp != null && bp.S >= 0.8 && (best == null || bp.S > best.S)) return AddOne(bp.E) + " (era un perk)";
            }
            if (Addons.Count >= 2) return "ya tiene 2 addons (decí \"quitar ...\" o \"quitar addons\")";
            string extra = "";
            // 2) con el killer anotado: buscar por lo que hace
            if (e == null && Killer != null)
            {
                List<Entry> ties;
                e = MatchDesc(text, pool, out ties);
                ties = ties.Where(x => !Addons.Contains(x)).ToList();
                if (e == null && ties.Count == 1) { e = ties[0]; doubtMode = true; }
                else if (e == null && ties.Count > 1)
                {
                    // dudoso: se anota el mas probable como sospecha y se ofrecen los otros para elegir ("el dos")
                    e = ties[0];
                    Pick = ties; PickEntry = e; PickUntil = Environment.TickCount + 30000;
                    Addons.Add(e);
                    if (!Doubt.Contains(e)) Doubt.Add(e);
                    return "+ addon: " + e.Name + " (?) · ¿cuál es? " + string.Join(" · ", ties.Select((x, i) => (i + 1) + " " + x.Name)) + " (decí \"el dos\"…)";
                }
                else if (e == null) extra = " (no lo reconocí entre los addons de " + Killer.Name + ")";
            }
            else if (e == null) extra = " (decí el killer para reconocerlo)";
            // 3) si no, queda la descripcion tal cual
            if (e == null) e = Note(text);
            if (Addons.Any(a => a.Name == e.Name)) return e.Name + " ya estaba";
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
            return "Dead by Daylight. Tiene Pain Resonance, Grim Embrace y Deadlock. " + string.Join(", ", names) +
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
            DeviceName = "predeterminado de Windows";
            if (!string.IsNullOrEmpty(device))
            {
                var devs = Devices();
                for (int i = 0; i < devs.Count; i++)
                {
                    string a = devs[i].Trim(), b = device.Trim();
                    if (a.Length > 0 && (b.StartsWith(a, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                    { id = i; DeviceName = devs[i]; break; }
                }
                if (id == WAVE_MAPPER) LastError = "no encontré \"" + device + "\", uso el predeterminado";
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
        }

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
                        if (OnStatus != null) OnStatus("conectado");
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
                    if (OnStatus != null) OnStatus("sin conexión (" + ex.Message + "), reintento…");
                    Thread.Sleep(backoff);
                    backoff = Math.Min(backoff * 2, 30000);
                }
            }
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
                        if (OnStatus != null) OnStatus("no pude enviar (" + ex.Message + "), reintento…");
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
            if (dll == null) return "no encontré steam_api64.dll de Dead by Daylight";
            SetDllDirectory(Path.GetDirectoryName(dll));
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
            var err = new byte[1024];
            int ir = SteamAPI_InitFlat(err);
            return ir == 0 ? null : "Steam no respondió (" + ir + ": " + Encoding.UTF8.GetString(err).TrimEnd('\0') + ")";
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
                                    if (!res.StartsWith("no ")) hits++;
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

    // ------------------------------------------------------------ actualizaciones desde GitHub (firmadas)
    static class Updater
    {
        // publicar.ps1 sube este numero en cada version
        public const int AppVersion = 16;
        public const string Base = "https://raw.githubusercontent.com/EzChe1997/dbd-killer-overlay/main/";
        // solo se aceptan versiones firmadas con la clave privada (que queda en la PC de quien publica)
        const string PublicKey = "<RSAKeyValue><Modulus>qWTK5n2m/nN/lqDh5zLLW1oiHJdRe/cC7tMzyMZCtKGh8dYbyKOvwDfQaNFVmqniOG9pYfP38o+QCnkjeaone7gXnt4H6M6DjB7F+ECvTBBH09TbDrrHGUKWNQryiwG0IeS0US8/iQfzOLiqeDamsyCbf3n1CHwVjOcK3G2Dhv6A0cX/vl3R1jLUAhTk5lfxzSF58la4VbGJsAEWH/943Lmy+BYj7pAVaPMkNy+1FiUmyejlWIIEBAn60CLhFY0jtyWVvyUt2wEqd3B7oKDgCRKGGEegmLp0n7yF3sHxXAfpyQd7VbrY/Tn5Hl6VybQ+1gmiTWFi2KiP1ZzA7fvg/Q==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // busca una version nueva; si la hay la baja, verifica firma y huella, y deja el exe nuevo en su lugar.
        // Devuelve la descripcion de la version instalada, o null si no habia nada nuevo.
        public static string CheckAndInstall(out string error)
        {
            error = null;
            try
            {
                byte[] man = Catalog.HttpBytes(Base + "update.json?t=" + DateTime.UtcNow.Ticks);
                string sig = Encoding.ASCII.GetString(Catalog.HttpBytes(Base + "update.sig?t=" + DateTime.UtcNow.Ticks)).Trim();
                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(PublicKey);
                    if (!rsa.VerifyData(man, "SHA256", Convert.FromBase64String(sig))) { error = "firma inválida (se ignora)"; return null; }
                }
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(man).TrimStart('﻿'));
                int ver = Convert.ToInt32(d["version"]);
                if (ver <= AppVersion) return null;
                var files = (Dictionary<string, object>)d["files"];
                string want = files["DbdOverlay.exe"].ToString();
                byte[] exe = Catalog.HttpBytes(Base + "DbdOverlay.exe?v=" + ver);
                string got;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    got = string.Concat(sha.ComputeHash(exe).Select(b => b.ToString("x2")));
                if (got != want) { error = "el archivo bajado no coincide con la firma (se ignora)"; return null; }
                // Windows deja renombrar el exe que esta corriendo: el viejo queda como .old y el nuevo toma su lugar
                string me = Application.ExecutablePath, old = me + ".old";
                try { if (File.Exists(old)) File.Delete(old); } catch { }
                File.Move(me, old);
                try { File.WriteAllBytes(me, exe); }
                catch { File.Move(old, me); throw; }
                object notes; d.TryGetValue("notes", out notes);
                return "versión " + ver + ((notes ?? "").ToString().Length > 0 ? ": " + notes : "");
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
            Field("language", "es");
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
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        readonly string dir;
        Cfg cfg; Catalog cat; State st; Mic mic;
        int port, vkTalk, vkClear, vkNames, vkHelp;
        bool helpShown;
        string HelpKeyName { get { return cfg.Get("TeclaAyuda", "H"); } }

        // texto, color, es titulo
        List<Tuple<string, Color, bool>> HelpLines()
        {
            Color head = Color.FromArgb(235, 90, 90), cmd = Color.WhiteSmoke, note = Color.FromArgb(150, 150, 160);
            var l = new List<Tuple<string, Color, bool>>();
            Action<string, Color, bool> A = (t, c, b) => l.Add(Tuple.Create(t, c, b));
            A("Mantené " + KeyName + " y decí:", head, true);
            A("\"tiene Pain Resonance y Deadlock\"   anota perks (máx. 4)", cmd, false);
            A("\"es la Huntress\"   killer (no se muestra, ayuda con los addons)", cmd, false);
            A("\"addon que ciega\"   addon por lo que hace (máx. 2); si duda: \"el dos\" o \"ninguno\"", cmd, false);
            A("\"quitar Deadlock\" · \"quitar addons\"", cmd, false);
            A("\"no, borrá eso\"   deshace lo último; al repetir no elige lo mismo", cmd, false);
            A("\"tiene el perk ...\"   busca solo entre perks", cmd, false);
            A("\"sí\"   cuando pregunta (¿...?): lo anota y aprende cómo lo dijiste", cmd, false);
            A("\"ofrenda Ebony Mori\" · \"trajo un mori\" · \"ofrenda de mapa de ...\"   ofrenda (una)", cmd, false);
            A("\"creo que tiene Deadlock\"   sospecha (se ve a medias con ?)", cmd, false);
            A("\"confirmado\" · \"descartado\"   la última sospecha (o \"confirmado Deadlock\")", cmd, false);
            A("\"apodo grimi es Grim Embrace\"   enseñar un apodo", cmd, false);
            A("\"mostrar nombres\" · \"ocultar nombres\"", cmd, false);
            A("Teclas", head, true);
            A(ClearKeyName + " (mantener 1 s)   borrar todo · un toque después = deshacer", cmd, false);
            A(HelpKeyName + " (mantener)   esta ayuda", cmd, false);
            if (!voice) A("Esta PC no tiene Whisper (solo ver): ícono → \"Instalar voz\"", Color.Orange, false);
            A(RoomText() + (room != null ? " · todos ven lo mismo" : ""), note, false);
            A("Más opciones: clic derecho en el ícono rojo \"D\" junto al reloj", note, false);
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
        System.Windows.Forms.Timer keyTimer, topTimer;


        // ---- iconos (se bajan de la wiki la primera vez y quedan en iconos\)
        readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();
        readonly HashSet<string> iconPending = new HashSet<string>();
        int IconSize { get { return (int)(fItem.Height * 1.75); } }

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
                        var data = Catalog.HttpBytes("https://deadbydaylight.wiki.gg/images/" + Uri.EscapeDataString(file));
                        File.WriteAllBytes(path, data);
                        BeginInvoke((Action)(() => { iconPending.Remove(file); Relayout(); }));
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
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // fondo transparente: la caja oscura se dibuja solo detras de los textos (los iconos quedan sueltos)
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            Opacity = cfg.GetD("Opacidad", 0.85);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            MakeFonts();

            vkTalk = Vk(cfg.Get("Tecla", "Alt"));
            if (vkTalk == 0) vkTalk = (int)Keys.Menu;
            vkClear = Vk(cfg.Get("TeclaLimpiar", "G"));
            vkNames = Vk(cfg.Get("TeclaNombres", ""));
            vkHelp = Vk(cfg.Get("TeclaAyuda", "H"));
            showNames = Regex.IsMatch(cfg.Get("Nombres", "no"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase);
            port = cfg.GetInt("Puerto", 8765);

            try { cat = Catalog.Load(Path.Combine(dir, "datos")); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "DBD Overlay"); Environment.Exit(1); }
            st = new State(cat);
            st.AliasPath = Path.Combine(dir, "apodos.txt");
            st.ClearKeyName = cfg.Get("TeclaLimpiar", "G");
            st.ReloadAliases();
            st.LoadFrom(Path.Combine(dir, "estado.txt"));

            Debug("==== INICIO " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\r\n" + cat.Info + ", apodos: " + st.AliasCount +
                  "\r\nteclas: hablar=" + KeyName + " borrar=" + cfg.Get("TeclaLimpiar", "G") + " ayuda=" + cfg.Get("TeclaAyuda", "H") +
                  " · microfono=" + cfg.Get("Microfono", "") + "\r\nestado cargado: " + st.Describe());
            SetupTray();
            if (!Preview) // --vista: solo dibuja (sin voz, sin sala, sin Steam) para probar el aspecto
            {
                StartVoice();
                if (ListenOn) StartListen();
                if (HandsOn) StartHands();
                StartRoom();
                UpdateRoomMenu();
                // sala automatica: mira cada 5 s si DBD esta abierto (el vigia de Steam solo vive mientras el juego corre)
                steamTimer = new System.Windows.Forms.Timer { Interval = 5000 };
                steamTimer.Tick += (s, e) => SteamTick();
                steamTimer.Start();
                SteamTick();
            }
            else voice = true;

            keyTimer = new System.Windows.Forms.Timer { Interval = 25 };
            keyTimer.Tick += (s, e) => KeyTick();
            keyTimer.Start();
            topTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            topTimer.Tick += (s, e) => { SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); };
            topTimer.Start();

            // descripcion al pasar el mouse (solo cuando el cursor esta visible: lobby, menu de ESC, fin de partida)
            hoverTimer = new System.Windows.Forms.Timer { Interval = 100 };
            hoverTimer.Tick += (s, e) => HoverTick();
            hoverTimer.Start();

            // actualizaciones de la app: solo al abrir
            if (Catalog.DataNews.Length > 0) Feedback(Catalog.DataNews, Color.FromArgb(150, 220, 150), 5000);
            if (!Preview)
            {
                Updater.CleanOld();
                CheckUpdate();
            }
            Relayout();
        }

        string KeyName { get { return cfg.Get("Tecla", "Alt"); } }

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
                mic.Start(cfg.Get("Microfono", ""));
                if (mic.LastError.Length > 0) Feedback("Micrófono: " + mic.LastError, Color.Orange, 8000);
                DebugEvent("micrófono: " + mic.DeviceName + (mic.LastError.Length > 0 ? " (" + mic.LastError + ")" : ""));
            }
            catch (Exception ex)
            {
                Feedback("No pude abrir el micrófono: " + ex.Message, Color.OrangeRed, 15000);
                DebugEvent("micrófono: no se pudo abrir: " + ex.Message);
            }
        }

        void StartWhisper(string exe, string model)
        {
            statusLine = "Cargando Whisper…";
            if (Whisper.Ping(port)) { serverReady = true; statusLine = ""; return; }
            try
            {
                int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2));
                var psi = new ProcessStartInfo(exe, "-m \"" + model + "\" --host 127.0.0.1 --port " + port + " -nt -t " + threads)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
                var p = Process.Start(psi);
                Job.Add(p);
                DebugEvent("Whisper arrancando: " + exe + " · modelo " + Path.GetFileName(model) + " · " + threads + " hilos");
            }
            catch (Exception ex) { statusLine = "Whisper no arrancó: " + ex.Message; return; }
            new Thread(() =>
            {
                for (int i = 0; i < 180 && !Whisper.Ping(port); i++) Thread.Sleep(500);
                bool ok = Whisper.Ping(port);
                BeginInvoke((Action)(() =>
                {
                    serverReady = ok;
                    statusLine = ok ? "" : "Whisper no responde";
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
            if (voice) { MessageBox.Show("Esta PC ya tiene Whisper: la voz ya funciona (mantené " + KeyName + " y hablá).", "DBD Overlay"); return; }
            bool nv = HasNvidia();
            const string rel = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/";
            string zipUrl = rel + (nv ? "whisper-bin-win-cuda-11.8.0-x64.zip" : "whisper-bin-x64.zip");
            string modelName = nv ? "ggml-large-v3-turbo-q5_0.bin" : "ggml-small-q5_1.bin";
            string modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + modelName;
            string wdir = Path.Combine(dir, "whisper"), mdir = Path.Combine(wdir, "models");
            string msg = (nv ? "Detecté una placa NVIDIA: voy a bajar Whisper con aceleración por GPU (rápido)."
                             : "No detecté placa NVIDIA: voy a bajar Whisper para el procesador (tarda un poco más en entender).") +
                         "\n\nDescarga: " + (nv ? "unos 860 MB" : "unos 200 MB") + " (desde GitHub y Hugging Face).\nSe guarda en " + wdir +
                         "\n\n¿Seguir?";
            if (MessageBox.Show(msg, "Instalar voz", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
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
                    Status("Descomprimiendo Whisper…");
                    using (var z = System.IO.Compression.ZipFile.OpenRead(zip))
                        foreach (var en in z.Entries)
                        {
                            if (string.IsNullOrEmpty(en.Name) || !Regex.IsMatch(en.Name, @"\.(exe|dll)$", RegexOptions.IgnoreCase)) continue;
                            System.IO.Compression.ZipFileExtensions.ExtractToFile(en, Path.Combine(wdir, en.Name), true); // sin la carpeta Release/
                        }
                    File.Delete(zip);
                    string model = Path.Combine(mdir, modelName);
                    if (!File.Exists(model)) Download(modelUrl, model, "modelo de voz");
                }
                catch (Exception ex) { err = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    installing = false; statusLine = "";
                    if (err != null)
                    {
                        Feedback("No se pudo instalar Whisper: " + err, Color.OrangeRed, 15000);
                        DebugEvent("instalar voz falló: " + err);
                        Relayout();
                        return;
                    }
                    DebugEvent("voz instalada");
                    Feedback("¡Voz instalada! Mantené " + KeyName + " y hablá", Color.FromArgb(150, 220, 150), 8000);
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
                        Status("Descargando " + what + "… " + (len > 0 ? pct + "%" : pct + " MB"));
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }

        // ---- idioma de los nombres y de las descripciones (por separado). Por voz se entienden los dos siempre.
        bool NamesEs { get { return cfg.Get("IdiomaNombres", "en").ToLowerInvariant().StartsWith("es"); } }
        bool DescEs { get { return !cfg.Get("IdiomaDescripciones", "es").ToLowerInvariant().StartsWith("en"); } }

        // ---- nombre del killer arriba: se puede ocultar y cambiar de tamano
        bool KillerOn { get { return !Regex.IsMatch(cfg.Get("MostrarKiller", "si"), "^(no|n|0|false)$", RegexOptions.IgnoreCase); } }
        Font killerFont;
        Font KillerFont()
        {
            float sz = Math.Max(5f, cfg.GetInt("TamanoKiller", 10) * UiScale);
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
                int cur = cfg.GetInt("TamanoKiller", 10);
                foreach (var o in m.DropDownItems.OfType<ToolStripMenuItem>()) if (o.Tag is int) o.Checked = (int)o.Tag == cur;
            };
            return m;
        }

        string DName(Entry e)
        {
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
        System.Windows.Forms.Timer hoverTimer;
        HoverTip tip;
        Entry hoverE;
        int hoverSince;

        void HoverTick()
        {
            // los candidatos de addon se van solos a los 30 s (queda anotado el primero como sospecha)
            if (st.Pick != null && Environment.TickCount - st.PickUntil > 0) { st.Pick = null; Relayout(); }
            Entry over = null; Rectangle rr = Rectangle.Empty;
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            // en partida DBD esconde el cursor: ahi no se muestra nada
            if (Visible && !helpShown && GetCursorInfo(ref ci) && (ci.flags & 1) != 0)
            {
                var p = new Point(ci.x - Left, ci.y - Top);
                foreach (var it in items)
                    if (it.E != null && !string.IsNullOrEmpty(DInfo(it.E)) && it.R.Contains(p)) { over = it.E; rr = it.R; }
            }
            if (over != hoverE) { hoverE = over; hoverSince = Environment.TickCount; if (over == null && tip != null) tip.Hide(); return; }
            if (over == null || Environment.TickCount - hoverSince < 250) return;
            if (tip != null && tip.Visible && tip.Current == over) return;
            if (tip == null) tip = new HoverTip(new Font("Segoe UI", fItem.Size, FontStyle.Bold), fSmall);
            string title = DName(over);
            string sub = over.K == Kind.Perk ? "Perk" + (over.Killer != null && over.Killer.Length > 0 ? " · " + over.Killer : "")
                       : over.K == Kind.Offering ? "Ofrenda" : "Addon" + (over.Killer != null && over.Killer.Length > 0 ? " · " + over.Killer : "");
            if (st.IsDoubt(over)) sub += " · sospecha (sin confirmar)";
            Color tc = over.K == Kind.Perk ? Color.WhiteSmoke : RarityColor(over.Rarity);
            var anchor = new Rectangle(Left + rr.X, Top + rr.Y, rr.Width, rr.Height);
            bool right = !cfg.Get("Posicion", "arriba-derecha").ToLowerInvariant().Contains("izq");
            tip.ShowFor(over, title, tc, sub, DInfo(over), anchor, Bounds, right);
        }

        class HoverTip : Form
        {
            public Entry Current;
            string title = "", sub = "", body = "";
            Color tc;
            readonly Font ft, fb, fs;
            const int Pad = 10, MaxW = 400;
            const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            public HoverTip(Font title, Font body)
            {
                ft = title; fb = body; fs = new Font(body.FontFamily, body.Size * 0.9f, FontStyle.Italic);
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                BackColor = Color.FromArgb(18, 18, 24); TopMost = true; DoubleBuffered = true; Opacity = 0.96;
            }
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80 | 0x8 | 0x08000000; return cp; } // transparente al mouse, sin activar
            }
            protected override bool ShowWithoutActivation { get { return true; } }

            public void ShowFor(Entry e, string t, Color c, string s, string b, Rectangle anchor, Rectangle overlay, bool leftOfOverlay)
            {
                Current = e; title = t; tc = c; sub = s; body = b;
                var big = new Size(MaxW, 4000);
                Size st = TextRenderer.MeasureText(title, ft, big, Wrap), ss = TextRenderer.MeasureText(sub, fs, big, Wrap), sb = TextRenderer.MeasureText(body, fb, big, Wrap);
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
                using (var pen = new Pen(Color.FromArgb(Math.Min(255, tc.R / 2 + 40), Math.Min(255, tc.G / 2 + 40), Math.Min(255, tc.B / 2 + 40)), 2))
                    g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                int y = Pad, w = Width - Pad * 2;
                var big = new Size(w, 4000);
                var r = new Rectangle(Pad, y, w, TextRenderer.MeasureText(title, ft, big, Wrap).Height);
                TextRenderer.DrawText(g, title, ft, r, tc, Wrap); y = r.Bottom + 2;
                r = new Rectangle(Pad, y, w, TextRenderer.MeasureText(sub, fs, big, Wrap).Height);
                TextRenderer.DrawText(g, sub, fs, r, Color.FromArgb(150, 150, 165), Wrap); y = r.Bottom + 8;
                r = new Rectangle(Pad, y, w, Height - y - Pad);
                TextRenderer.DrawText(g, body, fb, r, Color.Gainsboro, Wrap);
            }
        }

        // ---- actualizaciones: si hay version nueva firmada, la instala y reinicia (la lista se conserva)
        bool updating;

        void CheckUpdate()
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
                    if (what == null) return;
                    DebugEvent("actualización instalada (" + what + "): reinicio");
                    Feedback("DBD Overlay actualizado a la " + what + " · reiniciando…", Color.FromArgb(150, 220, 150), 4000);
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
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
        bool ListenOn { get { return Regex.IsMatch(cfg.Get("EscucharDiscord", "no"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase); } }

        void SetListen(bool v)
        {
            SetCfg("EscucharDiscord", v ? "si" : "no");
            if (listenItem != null) listenItem.Checked = v;
            if (v) StartListen(); else StopListen();
            Feedback(v ? "escuchando Discord: lo que digan con \"tiene…\" se anota como sospecha" : "ya no escucho Discord", Color.FromArgb(130, 190, 255), 5000);
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
            if (!voice) { Feedback("para escuchar Discord hace falta Whisper", Color.Orange, 5000); return; }
            var lb = new Loopback();
            lb.Start();
            loop = lb; listening = true;
            DebugEvent("discord: escuchando la salida de audio de la PC");
            new Thread(() =>
            {
                Thread.Sleep(800);
                if (lb.LastError.Length > 0) { BeginInvoke((Action)(() => { DebugEvent("discord: ERROR " + lb.LastError); Feedback("no pude escuchar el audio de la PC: " + lb.LastError, Color.OrangeRed, 8000); })); return; }
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
            Feedback(v ? "hablar sin tecla: decí \"tiene…\", \"creo que tiene…\", \"el killer es…\" (sí/no/quitar siguen con " + KeyName + ")" : "hablar sin tecla: apagado", Color.FromArgb(150, 220, 150), 6000);
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
            if (!voice) { Feedback("para hablar sin tecla hace falta Whisper", Color.Orange, 5000); return; }
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

        void OnHandsHeard(string text, double secs, int ms)
        {
            string raw = text.Trim(), nt = Txt.Norm(text);
            var sb = new StringBuilder("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (SIN TECLA) ====\r\n");
            sb.Append("micrófono: " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s, Whisper " + ms + " ms\r\n");
            sb.Append("whisper: \"" + raw + "\"\r\n");
            var m = HandsRx.Match(nt);
            if (!m.Success) { sb.Append("resultado: (no es un comando: se ignora)"); Debug(sb.ToString()); return; }
            if (raw.StartsWith("¿") || raw.EndsWith("?")) { sb.Append("resultado: (es una pregunta: se ignora)"); Debug(sb.ToString()); return; }
            if (Regex.IsMatch(nt.Substring(0, m.Index), @"\b(no|ni|nunca|tampoco)\s*$")) { sb.Append("resultado: (frase negativa: se ignora)"); Debug(sb.ToString()); return; }
            sb.Append("antes:   " + st.Describe() + "\r\n");
            var before = st.Snapshot();
            st.Trace.Clear();
            string what = st.Apply(m.Value);
            st.ForgetGuess(); // sin tecla no se aprenden apodos ni se pregunta "¿X?"
            foreach (var line in st.Trace) sb.Append(line + "\r\n");
            sb.Append("resultado: " + what + "\r\ndespues: " + st.Describe());
            Debug(sb.ToString());
            Log("[sin tecla] " + raw, what);
            if (what.StartsWith("no ") || what.Contains("ya estaba")) return; // lo que no sirvio no molesta
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
            if (!m.Success) { sb.Append("resultado: (no es un \"tiene…\": se ignora)"); Debug(sb.ToString()); return; }
            sb.Append("antes:   " + st.Describe() + "\r\n");
            var before = st.Snapshot();
            st.Trace.Clear();
            string what = st.Apply("creo que " + m.Value); // lo que dicen otros entra como sospecha
            st.ForgetGuess();
            foreach (var line in st.Trace) sb.Append(line + "\r\n");
            sb.Append("resultado: " + what + "\r\ndespues: " + st.Describe());
            Debug(sb.ToString());
            Log("[discord] " + text, what);
            if (what.StartsWith("no ") || what.Contains("ya estaba")) return; // no molestar con lo que no sirvio
            st.Save(Path.Combine(dir, "estado.txt"));
            PublishDiff(before);
            Feedback("Discord: " + what, Color.FromArgb(130, 190, 255), 6000);
        }

        // ---- sala compartida (ntfy.sh): cada cambio se manda como diferencia y todas las PCs lo aplican en el mismo orden
        Room room;
        string roomStatus = "";
        ToolStripMenuItem roomLabel, roomCopy, roomLeave, autoItem;

        string MyName { get { return cfg.Get("Nombre", ""); } }

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
        System.Windows.Forms.Timer steamTimer;

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
            partyNames = new Dictionary<string, string>();
            partyCount = party != null ? party.Count : 0;
            if (party != null) foreach (var kv in party) partyNames[Fingerprint(kv.Key)] = (kv.Value ?? "").ToString();
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
            r.Start(topic);
            DebugEvent("sala: entrando a " + (ManualRoom ? "la sala " + cfg.Get("Sala", "") : "la del grupo de Steam") + (MyName.Length > 0 ? " como " + MyName : ""));
            UpdateRoomMenu();
        }

        // quien mando un cambio: nombre propio si lo puso, si no el nombre de Steam del amigo (resuelto en esta PC)
        string SenderName(Dictionary<string, object> op)
        {
            object o; string n;
            if (op.TryGetValue("name", out o) && (n = (o ?? "").ToString()).Length > 0) return n;
            if (op.TryGetValue("h", out o) && partyNames.TryGetValue((o ?? "").ToString(), out n) && n.Length > 0) return n;
            return "alguien del grupo";
        }

        Dictionary<string, object> Stamp(Dictionary<string, object> op)
        {
            op["name"] = MyName;
            if (!ManualRoom && steamMe.Length > 0 && steamLobby.Length > 0) op["h"] = Fingerprint(steamMe);
            return op;
        }

        void OnRoomOp(Dictionary<string, object> op, Room r)
        {
            object from, name, h;
            op.TryGetValue("from", out from);
            op.TryGetValue("name", out name);
            op.TryGetValue("h", out h);
            bool mine = (from ?? "").ToString() == r.Me
                     || (steamMe.Length > 0 && steamLobby.Length > 0 && (h ?? "").ToString() == Fingerprint(steamMe))
                     || (MyName.Length > 0 && (name ?? "").ToString() == MyName);
            string what = st.ApplyRemote(op);
            if (what.Length == 0) return; // ya estaba aplicado (por ejemplo el eco de lo propio)
            st.Save(Path.Combine(dir, "estado.txt"));
            string who = SenderName(op);
            DebugEvent("sala: " + (mine ? "(propio) " : who + ": ") + what);
            if (!mine) Feedback(who + ": " + what, Color.FromArgb(130, 190, 255), 5000);
            else Relayout();
        }

        void PublishDiff(object before)
        {
            if (room == null || before == null) return;
            var op = st.Diff(before);
            if (op == null) return;
            room.Publish(Stamp(op));
        }

        void PublishReset()
        {
            if (room == null) return;
            room.Publish(Stamp(new Dictionary<string, object> { { "reset", true } }));
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
                "¿Con qué nombre te ven los demás cuando anotás algo?\n\n(En la sala automática de Steam, si lo dejás vacío tus amigos ven tu nombre de Steam, sin que se mande por internet.)",
                "Sala compartida", MyName).Trim();
            if (n.Length == 0) return MyName.Length > 0;
            SetCfg("Nombre", n.Length > 20 ? n.Substring(0, 20) : n);
            return true;
        }

        void CreateRoom()
        {
            if (MyName.Length == 0 && !AskName()) return;
            string code = Room.NewCode();
            SetCfg("Sala", code);
            StopSteam();
            StartRoom();
            PublishAll();
            try { Clipboard.SetText(code); } catch { }
            MessageBox.Show("Sala creada. Código: " + code + "\n\n(ya está copiado: pegalo en el chat)\n\n" +
                            "Tus amigos: clic derecho en el ícono \"D\" → Sala compartida → Unirse con código… y pegan el código.\n\n" +
                            "Mientras uses código, la sala automática de Steam queda en pausa.",
                            "Sala compartida", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void JoinRoom()
        {
            string code = Room.CleanCode(Microsoft.VisualBasic.Interaction.InputBox(
                "Pegá el código de la sala que te pasó tu amigo:", "Unirse con código", ""));
            if (code.Length == 0) return;
            if (code.Length < 6) { MessageBox.Show("Ese código no parece válido.", "Sala compartida"); return; }
            if (MyName.Length == 0 && !AskName()) return;
            st.Clear(); // lo que hay en la sala reemplaza lo local
            st.Save(Path.Combine(dir, "estado.txt"));
            SetCfg("Sala", code);
            StopSteam();
            StartRoom();
            Feedback("entraste a la sala " + code, Color.FromArgb(130, 190, 255), 5000);
        }

        void LeaveRoom()
        {
            SetCfg("Sala", "");
            DebugEvent("sala: salí de la sala con código");
            StartRoom(); // si corresponde, vuelve la automatica (el vigia arranca en el proximo tick)
            SteamTick();
            Feedback("saliste de la sala con código" + (AutoRoom ? " · vuelve la automática de Steam" : ""), Color.Silver, 5000);
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
            string conn = room == null ? "" : " · " + (roomStatus.Length > 0 ? roomStatus : "conectando…");
            if (ManualRoom) return "Sala con código " + cfg.Get("Sala", "") + conn;
            if (!AutoRoom) return "Sin sala";
            if (steamErr.Length > 0) return "Steam: " + steamErr;
            if (steamProc == null) return "Automática: esperando que abras DBD";
            if (steamLobby.Length == 0) return "Automática: esperando grupo de Steam";
            return "Grupo de Steam" + (partyCount > 0 ? " (+" + partyCount + " amigo" + (partyCount > 1 ? "s" : "") + ")" : " (solo vos)") + conn;
        }

        void UpdateRoomMenu()
        {
            if (roomLabel == null) return;
            roomLabel.Text = RoomText();
            autoItem.Checked = AutoRoom;
            roomCopy.Enabled = roomLeave.Enabled = ManualRoom;
            string t = "DBD Overlay · " + RoomText();
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            Relayout();
        }

        void SetupTray()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(170, 20, 30));
                using (var f = new Font("Segoe UI", 8, FontStyle.Bold))
                    TextRenderer.DrawText(g, "D", f, new Rectangle(0, 0, 16, 16), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            tray = new NotifyIcon { Icon = Icon.FromHandle(bmp.GetHicon()), Text = "DBD Overlay", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Borrar lista", null, (s, e) => ClearWithUndo());
            namesItem = new ToolStripMenuItem("Mostrar nombres") { Checked = showNames };
            namesItem.Click += (s, e) => SetNames(!showNames);
            menu.Items.Add(namesItem);
            var roomMenu = new ToolStripMenuItem("Sala compartida");
            roomLabel = new ToolStripMenuItem("Sin sala") { Enabled = false };
            roomMenu.DropDownItems.Add(roomLabel);
            roomMenu.DropDownItems.Add("-");
            autoItem = new ToolStripMenuItem("Automática con mi grupo de Steam") { Checked = AutoRoom };
            autoItem.Click += (s, e) => SetAuto(!AutoRoom);
            roomMenu.DropDownItems.Add(autoItem);
            roomMenu.DropDownItems.Add("-");
            roomMenu.DropDownItems.Add(new ToolStripMenuItem("Con código (para quien no juega por Steam):") { Enabled = false });
            roomMenu.DropDownItems.Add("Crear sala con código", null, (s, e) => CreateRoom());
            roomMenu.DropDownItems.Add("Unirse con código…", null, (s, e) => JoinRoom());
            roomCopy = new ToolStripMenuItem("Copiar código", null, (s, e) =>
            {
                try { Clipboard.SetText(cfg.Get("Sala", "")); Feedback("código copiado: " + cfg.Get("Sala", ""), Color.Silver, 3000); } catch { }
            });
            roomMenu.DropDownItems.Add(roomCopy);
            roomMenu.DropDownItems.Add("Cambiar mi nombre…", null, (s, e) => AskName());
            roomLeave = new ToolStripMenuItem("Salir de la sala con código", null, (s, e) => LeaveRoom());
            roomMenu.DropDownItems.Add(roomLeave);
            menu.Items.Add(roomMenu);
            listenItem = new ToolStripMenuItem("Escuchar Discord (prueba)") { Checked = ListenOn };
            listenItem.Click += (s, e) => SetListen(!ListenOn);
            menu.Items.Add(listenItem);
            handsItem = new ToolStripMenuItem("Hablar sin tecla (prueba)") { Checked = HandsOn };
            handsItem.Click += (s, e) => SetHands(!HandsOn);
            menu.Items.Add(handsItem);
            // tamaño de los íconos (los addons y la ofrenda se escalan junto con los perks)
            var sizeMenu = new ToolStripMenuItem("Tamaño de los íconos");
            var sizes = new[] { Tuple.Create("Chico", 40), Tuple.Create("Normal", 48), Tuple.Create("Grande", 60), Tuple.Create("Muy grande", 72), Tuple.Create("Enorme", 88), Tuple.Create("Gigante", 104) };
            foreach (var sz in sizes)
            {
                var it = new ToolStripMenuItem(sz.Item1 + " (" + sz.Item2 + " px)") { Tag = sz.Item2 };
                it.Click += (s, e) =>
                {
                    int px = (int)((ToolStripMenuItem)s).Tag;
                    SetCfg("TamanoPerk", px.ToString());
                    foreach (ToolStripMenuItem o in sizeMenu.DropDownItems) o.Checked = (int)o.Tag == px;
                    Relayout();
                    Feedback("tamaño de íconos: " + ((ToolStripMenuItem)s).Text, Color.Silver, 3000);
                };
                sizeMenu.DropDownItems.Add(it);
            }
            sizeMenu.DropDownOpening += (s, e) => { foreach (ToolStripMenuItem o in sizeMenu.DropDownItems) o.Checked = (int)o.Tag == PerkSize; };
            menu.Items.Add(sizeMenu);
            // escala de todo el widget (iconos, letras y nombre del killer a la vez)
            var scaleMenu = new ToolStripMenuItem("Escala general");
            foreach (int pc in new[] { 50, 60, 75, 90, 100, 115, 130, 150, 175, 200 })
            {
                var it = new ToolStripMenuItem(pc + " %") { Tag = pc };
                it.Click += (s, e) =>
                {
                    SetCfg("Escala", ((int)((ToolStripMenuItem)s).Tag).ToString());
                    MakeFonts();
                    Relayout();
                    Feedback("escala: " + ((ToolStripMenuItem)s).Text, Color.Silver, 3000);
                };
                scaleMenu.DropDownItems.Add(it);
            }
            scaleMenu.DropDownOpening += (s, e) => { foreach (ToolStripMenuItem o in scaleMenu.DropDownItems) o.Checked = (int)o.Tag == cfg.GetInt("Escala", 100); };
            menu.Items.Add(scaleMenu);
            menu.Items.Add(KillerMenu());
            var langMenu = new ToolStripMenuItem("Idioma");
            langMenu.DropDownItems.Add(LangMenu("Nombres", "IdiomaNombres", "en"));
            langMenu.DropDownItems.Add(LangMenu("Descripciones", "IdiomaDescripciones", "es"));
            menu.Items.Add(langMenu);
            var voiceItem = new ToolStripMenuItem("Instalar voz (Whisper)…", null, (s, e) => InstallVoice());
            menu.Items.Add(voiceItem);
            // si ya esta instalado (o instalandose) el boton queda deshabilitado
            menu.Opening += (s, e) =>
            {
                voiceItem.Enabled = !voice && !installing;
                voiceItem.Text = voice ? "Voz (Whisper) instalada ✓" : installing ? "Instalando voz (Whisper)…" : "Instalar voz (Whisper)…";
            };
            menu.Items.Add("Abrir configuración", null, (s, e) => { try { Process.Start("notepad.exe", "\"" + Path.Combine(dir, "config.ini") + "\""); } catch { } });
            menu.Items.Add("Editar apodos", null, (s, e) => { try { st.ReloadAliases(); Process.Start("notepad.exe", "\"" + Path.Combine(dir, "apodos.txt") + "\""); } catch { } });
            menu.Items.Add("Abrir carpeta", null, (s, e) => { try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { } });
            menu.Items.Add("-");
            menu.Items.Add("Salir", null, (s, e) => Close());
            tray.ContextMenuStrip = menu;
        }

        void Feedback(string t, Color c, int ms)
        {
            feedback = t; feedbackColor = c; feedbackUntil = Environment.TickCount + ms;
            Relayout();
        }

        void Changed(string what)
        {
            st.Save(Path.Combine(dir, "estado.txt"));
            Feedback(what, Color.FromArgb(150, 220, 150), 5000);
        }

        void KeyTick()
        {
            int now = Environment.TickCount;
            bool down = (GetAsyncKeyState(vkTalk) & 0x8000) != 0;

            if (mic != null && !mic.Running && micRestartAt < 0)
            {
                Feedback("Micrófono: " + (mic.LastError.Length > 0 ? mic.LastError : "se cortó") + " (reintento…)", Color.OrangeRed, 6000);
                DebugEvent("micrófono: " + (mic.LastError.Length > 0 ? mic.LastError : "se cortó") + " (reintento en 3 s)");
                micRestartAt = now + 3000;
            }
            if (micRestartAt >= 0 && now - micRestartAt >= 0) { micRestartAt = -1; StartMic(); }

            if (!voice)
            {
                if (down && !talkWarned)
                {
                    talkWarned = true;
                    Feedback(installing ? "instalando la voz, esperá…" : "esta PC no tiene Whisper: instalalo desde el ícono \"D\" (por ahora solo ver)", Color.Orange, 6000);
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
                bool c = (GetAsyncKeyState(vkClear) & 0x8000) != 0;
                if (c && !clearDown) { clearStart = now; clearFired = false; clearHint = false; }
                if (c && !clearFired)
                {
                    int held = now - clearStart;
                    if (held >= 1000) { clearFired = true; ClearWithUndo(); }
                    else if (held >= 250 && !clearHint && HasAnything())
                    {
                        clearHint = true;
                        Feedback("mantené " + ClearKeyName + " para borrar todo…", Color.FromArgb(240, 170, 80), 1500);
                    }
                }
                if (!c && clearDown && !clearFired && undoSnap != null && now - undoUntil < 0)
                {
                    var before = st.Snapshot();
                    st.Restore(undoSnap);
                    undoSnap = null;
                    PublishDiff(before);
                    Changed("borrado deshecho");
                    DebugEvent("borrado deshecho con tecla");
                }
                clearDown = c;
            }
            if (vkHelp != 0)
            {
                bool hk = (GetAsyncKeyState(vkHelp) & 0x8000) != 0;
                if (hk != helpShown) { helpShown = hk; Relayout(); }
            }
            if (vkNames != 0)
            {
                bool c = (GetAsyncKeyState(vkNames) & 0x8000) != 0;
                if (c && !namesDown) SetNames(!showNames);
                namesDown = c;
            }

            if (feedback.Length > 0 && now - feedbackUntil >= 0) { feedback = ""; Relayout(); }
        }

        int clearStart, undoUntil;
        bool clearFired, clearHint;
        object undoSnap;
        string ClearKeyName { get { return cfg.Get("TeclaLimpiar", "G"); } }

        bool HasAnything() { return st.Killer != null || st.Perks.Count > 0 || st.Addons.Count > 0 || st.Offering != null; }

        void ClearWithUndo()
        {
            if (!HasAnything()) { Feedback("la lista ya está vacía", Color.Silver, 2000); return; }
            undoSnap = st.Snapshot();
            undoUntil = Environment.TickCount + 8000;
            DebugEvent("lista borrada con tecla o menú (antes de borrar)");
            st.Clear();
            st.Save(Path.Combine(dir, "estado.txt"));
            PublishReset();
            Feedback("lista borrada · tocá " + ClearKeyName + " para deshacer", Color.FromArgb(150, 220, 150), 8000);
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
            if (!serverReady) { Feedback("Whisper todavía está cargando", Color.Orange, 3000); DebugEvent("audio ignorado: Whisper cargando"); return; }
            if (samples.Length < 16000 * 6 / 10)
            {
                Feedback("muy corto, mantené la tecla mientras hablás", Color.Orange, 3000);
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
                Feedback("no se escuchó voz (¿micrófono silenciado?)", Color.FromArgb(240, 170, 80), 4000);
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
                        Feedback("Error de Whisper: " + err, Color.OrangeRed, 6000);
                        sb.Append("ERROR de Whisper: " + err);
                        Debug(sb.ToString());
                        return;
                    }
                    sb.Append("whisper: \"" + text + "\"\r\n");
                    sb.Append("antes:   " + st.Describe() + "\r\n");
                    // "mostrar nombres" / "ocultar nombres" / "nombres"
                    string nt = Txt.Norm(text);
                    if (Regex.IsMatch(nt, @"\bnombres?\b") && nt.Split(' ').Length <= 4)
                    {
                        bool v = Regex.IsMatch(nt, @"\b(ocult|sac|quit|escond|sin)") ? false
                               : Regex.IsMatch(nt, @"\b(mostr|ver|pon|activ|con)") ? true : !showNames;
                        SetNames(v);
                        Feedback(v ? "nombres visibles" : "nombres ocultos", Color.FromArgb(150, 220, 150), 3000);
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
                    bool ok = !what.StartsWith("no ");
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
        class Item { public Rectangle R; public Entry E; public bool Slot; public int Num; }
        class TextItem { public string T; public Font F; public Color C; public Point P; public int W; public bool Block, Center; }
        List<Item> items = new List<Item>();
        List<TextItem> texts = new List<TextItem>();
        bool showNames;
        // escala general del widget (iconos, letras, killer): Escala=100 es el tamano normal
        float UiScale { get { return Math.Max(30, Math.Min(300, cfg.GetInt("Escala", 100))) / 100f; } }
        int PerkSize { get { return Math.Max(16, (int)(Math.Max(24, cfg.GetInt("TamanoPerk", 48)) * UiScale)); } }

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

        void Relayout()
        {
            items = new List<Item>();
            texts = new List<TextItem>();
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Func<string, Font, Size> M = (t, f) => TextRenderer.MeasureText(t, f, new Size(int.MaxValue, int.MaxValue), flags);
            Color dim = Color.FromArgb(140, 140, 150);
            bool any = st.Perks.Count > 0 || st.Addons.Count > 0 || st.Offering != null; // el killer no se muestra
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
                    nameLines.Add(Tuple.Create("✦ ofrenda: " + st.Offering.Name + (st.IsDoubt(st.Offering) ? " (?)" : ""), st.IsDoubt(st.Offering) ? Color.FromArgb(240, 190, 60) : Color.Gainsboro));

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
                    items.Add(new Item { R = new Rectangle(centers[i].X - D / 2, centers[i].Y - D / 2, D, D), E = i < st.Perks.Count ? st.Perks[i] : null, Slot = true });
                blockW = x + clusterW - pad;
                y = top + blockH + 6;
            }
            int blockItems = items.Count, blockTexts = texts.Count;

            // addon dudoso: los candidatos numerados para elegir ("el dos")
            var pickItems = new List<Item>(); TextItem pickText = null; int rowW = 0;
            if (st.Pick != null && Environment.TickCount - st.PickUntil < 0)
            {
                for (int i = 0; i < st.Pick.Count; i++)
                    pickItems.Add(new Item { R = new Rectangle(i * (A + 8), y, A, A), E = st.Pick[i], Num = i + 1 });
                rowW = st.Pick.Count * (A + 8) - 8;
                string pt = "¿cuál? " + string.Join(" · ", st.Pick.Select((x, i) => (i + 1) + " " + DName(x)));
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
            if (talking) bottom.Add(Tuple.Create("● Escuchando…", Color.FromArgb(255, 80, 80)));
            else if (busy) bottom.Add(Tuple.Create("… Procesando", Color.Khaki));
            if (statusLine.Length > 0) bottom.Add(Tuple.Create(statusLine, Color.Orange));
            if (feedback.Length > 0) bottom.Add(Tuple.Create(feedback, feedbackColor));
            if (!any && ktext == null && bottom.Count == 0 && !helpShown)
                bottom.Add(Tuple.Create((voice ? KeyName + " hablar" : "solo ver") + " · " + HelpKeyName + " ayuda" +
                                        (room == null ? "" : ManualRoom ? " · sala " + cfg.Get("Sala", "") : ""), dim));

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

            var scr = Screen.PrimaryScreen.Bounds;
            int mx = cfg.GetInt("MargenX", 30), my = cfg.GetInt("MargenY", 140);
            int wx = right ? scr.Right - mx - size.Width : scr.Left + mx;
            int wy = pos.Contains("abajo") ? scr.Bottom - my - size.Height : scr.Top + my;
            Bounds = new Rectangle(wx, wy, size.Width, size.Height);
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
            foreach (var it in items)
            {
                if (it.E != null) DrawIcon(g, it.E, it.R);
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
                if (e.K == Kind.Perk) ir.Inflate(-r.Width / 9, -r.Height / 9); // que el dibujo quede dentro del rombo
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
            }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
            using (var mx = new Mutex(true, "DbdOverlay_SingleInstance", out created))
            {
                if (!created) { MessageBox.Show("DBD Overlay ya está abierto (mirá el ícono rojo junto al reloj).", "DBD Overlay"); return; }
                Application.EnableVisualStyles();
                Application.Run(new Overlay(dir));
            }
        }
    }
}
