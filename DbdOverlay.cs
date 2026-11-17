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
        // lista de mapas de hens333.com/callouts: la pagina guarda la lista en sus archivos .js
        // ({realm:"…",names:["…"],image:"Carpeta/Mapa.webp"}). Se guarda en hens_mapas.txt: "nombres|…<tab>imagen<tab>reino".
        public const string HensBase = "https://hens333.com/";
        static List<string> HensMapList(string dir)
        {
            string path = Path.Combine(dir, "hens_mapas.txt");
            bool stale = !File.Exists(path) || (remoteVer != null ? Changed("maps") : (DateTime.Now - File.GetLastWriteTime(path)).TotalDays > 7);
            if (stale)
            {
                try
                {
                    string html = Encoding.UTF8.GetString(HttpBytes(HensBase + "callouts"));
                    var byImage = new Dictionary<string, string[]>(); // imagen -> { nombres, reino }
                    var order = new List<string>();
                    foreach (Match jm in Regex.Matches(html, @"_app/immutable/nodes/[A-Za-z0-9_.-]+\.js"))
                    {
                        string js = Encoding.UTF8.GetString(HttpBytes(HensBase + jm.Value));
                        foreach (Match m in Regex.Matches(js, @"\{(?:realm:""([^""]*)"",)?names:\[([^\]]*)\],image:""([^""]+)""\}"))
                        {
                            string img = m.Groups[3].Value, realm = m.Groups[1].Value;
                            var names = Regex.Matches(m.Groups[2].Value, @"""([^""]*)""").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
                            string[] cur;
                            if (!byImage.TryGetValue(img, out cur)) { byImage[img] = new[] { string.Join("|", names), realm }; order.Add(img); }
                            else if (cur[1].Length == 0 && realm.Length > 0) cur[1] = realm;
                        }
                    }
                    if (order.Count > 0) File.WriteAllLines(path, order.Select(i => byImage[i][0] + "\t" + i + "\t" + byImage[i][1]), Encoding.UTF8);
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
            var perkIcons = KeyMap(WikiList(dir, "wiki_perks2.txt", "IconPerks_|T_UI_iconsPerks_"));
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
                var offIcons = KeyMap(WikiList(dir, "wiki_ofrendas2.txt", "IconFavors_|T_UI_iconsFavors_"));
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

    // ------------------------------------------------------------ estado + interpretacion
    class State
    {
        public Entry Killer;
        public List<Entry> Perks = new List<Entry>(), Addons = new List<Entry>();
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
            undoSnap = before; undoAdded = new List<Entry> { e }; undoAlias = null;
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
            if (!ok) return "no estaba: " + e.Name;
            undoSnap = before; undoAdded = null; undoAlias = null;
            return "- " + e.Name;
        }

        // cambiar uno por otro en el mismo lugar (el nuevo hereda si era sospecha, salvo que se diga lo contrario)
        public string ReplacePicked(Entry old, Entry neu, bool doubt)
        {
            var before = (Snap)Snapshot();
            var list = old.K == Kind.Perk ? Perks : old.K == Kind.Addon ? Addons : null;
            if (old.K == Kind.Offering) Offering = neu;
            else if (list == null || !list.Contains(old)) return "no estaba: " + old.Name;
            else if (list.Contains(neu)) list.Remove(old);
            else list[list.IndexOf(old)] = neu;
            Doubt.Remove(old);
            if (doubt) Doubt.Add(neu); else Doubt.Remove(neu);
            undoSnap = before; undoAdded = new List<Entry> { neu }; undoAlias = null;
            return old.Name + " → " + neu.Name + (doubt ? " (?)" : "");
        }

        // sospecha <-> seguro
        public string ToggleDoubt(Entry e)
        {
            var before = (Snap)Snapshot();
            if (!Current().Contains(e)) return "no estaba: " + e.Name;
            undoSnap = before; undoAdded = null; undoAlias = null;
            if (Doubt.Remove(e)) return "✓ " + e.Name + " confirmado";
            Doubt.Add(e);
            return e.Name + " ahora es sospecha (?)";
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
            return "killer: " + k.Name + (had && n > 0 ? " · addons borrados" : "");
        }

        string AddOne(Entry e)
        {
            if (e.K == Kind.Killer)
            {
                if (Killer == e) return "el killer ya es " + e.Name;
                // ya habia otro killer: se pide confirmacion (cambiar borra los addons del anterior)
                if (Killer != null && !ConfirmedKiller)
                {
                    PendingKiller = e; PendingUntil = Environment.TickCount + 20000;
                    return "¿cambiar " + Killer.Name + " por " + e.Name + "? se borran los addons · decí/escribí \"sí\"";
                }
                return ChangeKiller(e);
            }
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
            if (bareNo && PendingKiller != null)
            {
                PendingKiller = null;
                return "ok, sigue " + (Killer != null ? Killer.Name : "sin killer");
            }
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
            if (t.Length == 0) return "no escuché nada";
            // borrar todo NO se hace por voz (Whisper podria entenderlo sin querer): se usa la tecla
            if (Regex.IsMatch(t, @"\b(nueva partida|otra partida|borra(r)? todo|limpia(r)?( todo)?|reinicia(r)?|reset)\b"))
                return "no borro por voz: mantené " + ClearKeyName + " 1 segundo";
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
                    int idx = w == "uno" || w == "1" || w.StartsWith("primer") ? 0 : w == "dos" || w == "2" || w.StartsWith("segund") ? 1
                            : w == "cuatro" || w == "4" || w.StartsWith("cuart") ? 3 : w == "cinco" || w == "5" || w.StartsWith("quint") ? 4 : 2;
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
            switch (rar) { case "visceral": return "rojo"; case "veryrare": return "violeta"; case "rare": return "verde"; case "uncommon": return "amarillo"; default: return "marrón"; }
        }

        static string ColorPlural(string rar) { return rar == "common" ? "marrones" : ColorName(rar) + "s"; }

        // varios candidatos: se anota el primero como sospecha y se elige con "el dos"
        string OfferPick(List<Entry> cands, string why)
        {
            var e = cands[0];
            Pick = cands; PickEntry = e; PickUntil = Environment.TickCount + 30000;
            Addons.Add(e);
            if (!Doubt.Contains(e)) Doubt.Add(e);
            return "+ addon: " + e.Name + " (?) · " + (why.Length > 0 ? why + " " : "") + "¿cuál es? " + string.Join(" · ", cands.Select((x, i) => (i + 1) + " " + x.Name)) + " (decí \"el dos\"…)";
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
                if (Addons.Count >= 2) return "ya tiene 2 addons (decí \"quitar ...\" o \"quitar addons\")";
                var byColor = KillerAddons().Where(a => a.Rarity == rar && !Addons.Contains(a)).ToList();
                if (byColor.Count == 0) return "no hay addons " + ColorPlural(rar) + " de " + Killer.Name;
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
                if (ce == null) return OfferPick(byColor.Take(5).ToList(), "hay " + byColor.Count + " " + ColorPlural(rar) + ":");
                Addons.Add(ce);
                if (doubtMode) Doubt.Add(ce);
                return "+ addon: " + ce.Name + (doubtMode ? " (?)" : "");
            }
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
                else if (e == null && ties.Count > 1) return OfferPick(ties, ""); // dudoso: elegir con "el dos"
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
            if (s.StartsWith("sala") && s.Length >= 14) s = s.Substring(4); // "sala abcd2345ef" (como se pega en el chat)
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
        public const int AppVersion = 40;
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
                // GitHub guarda copias de los archivos hasta 5 min: se pide el ultimo commit y se bajan los archivos
                // de ESE commit (direccion nueva = nunca una copia vieja). Si falla, la direccion de siempre.
                string ub = Base;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/EzChe1997/dbd-killer-overlay/commits/main");
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
                    if (!rsa.VerifyData(man, "SHA256", Convert.FromBase64String(sig))) { error = "firma inválida (se ignora)"; return null; }
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
            title = k != null ? "Lo más visto contra " + DName(k) : "Lo más visto últimamente";
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
            if (score.Count == 0 && k != null) title = "Perks de " + DName(k);
            return res;
        }

        public string HelpKeyName { get { return cfg.Get("TeclaAyuda", "Shift+H"); } }
        string TypeKeyText { get { return cfg.Get("TeclaEscribir", "Shift+T"); } }

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
                A((InGroup ? "👥 En grupo" + who : "Sin grupo") + " · " + RoomText(), InGroup ? Color.FromArgb(130, 200, 140) : note, false);
            }
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
            mapWin.Opacity = MapOpacity / 100.0;
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
            Feedback("tamaño del mapa: " + px + " px", Color.Silver, 3000);
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
        int MapOpacity { get { return Math.Max(15, Math.Min(100, cfg.GetInt("OpacidadMapa", 90))); } }
        public void SetMapOpacity(int pc)
        {
            pc = Math.Max(15, Math.Min(100, pc));
            SetCfg("OpacidadMapa", pc.ToString());
            if (mapWin != null) mapWin.Opacity = pc / 100.0;
            Feedback("opacidad del mapa: " + pc + " %", Color.Silver, 3000);
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

        class MapWindow : Form
        {
            readonly Overlay ov;
            string title = "", sub = "";
            List<string> files = new List<string>();
            int side = 320, px, py;
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
            public void Set(string t, string s, List<string> f, int size) { title = t; sub = s; files = f; side = Math.Max(160, Math.Min(900, size)); }
            public void Place(int x, int y) { px = x; py = y; Fit(); }
            // alto segun la imagen (las de hens no son cuadradas)
            public void Fit()
            {
                var img = files.Count > 0 ? ov.MapImage(files[0]) : null;
                int h = img != null ? side * img.Height / Math.Max(1, img.Width) : side;
                var scr = Screen.PrimaryScreen.Bounds;
                Bounds = new Rectangle(scr.Left + px, scr.Top + py, side + 12, h + 12);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                var img = files.Count > 0 ? ov.MapImage(files[0]) : null;
                var r = new Rectangle(6, 6, side, Height - 12);
                if (img != null) g.DrawImage(img, r);
                else using (var fs = new Font("Segoe UI", 9f))
                    TextRenderer.DrawText(g, ov.MapError != null ? "No pude bajar el plano de " + title + "\n(" + ov.MapError + ")" : "cargando " + title + "…", fs, r, Color.FromArgb(160, 160, 175), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                using (var pen = new Pen(Color.FromArgb(70, 70, 90))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        // imagen del mapa (hens333.com, formato WebP): se baja una vez, se pasa a PNG (Windows abre WebP con su
        // decodificador) achicada a 900 px de ancho y queda guardada en "mapas".
        readonly Dictionary<string, Image> mapCache = new Dictionary<string, Image>();
        readonly HashSet<string> mapPending = new HashSet<string>();
        public string MapError;
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
            if (mapPending.Add(file))
            {
                MapError = null;
                new Thread(() =>
                {
                    string err = null;
                    try
                    {
                        Directory.CreateDirectory(folder);
                        string url = Catalog.HensBase + "img/dbd/callouts/" + string.Join("/", file.Split('/').Select(Uri.EscapeDataString));
                        var data = Catalog.HttpBytes(url);
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
                        if (err != null) { MapError = err; DebugEvent("mapa: no pude bajar " + file + " (" + err + ")"); }
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
            MigrateConfig();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // fondo transparente: la caja oscura se dibuja solo detras de los textos (los iconos quedan sueltos)
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            Opacity = cfg.GetD("Opacidad", 0.85);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            MakeFonts();

            string tn; ParseCombo(cfg.Get("Tecla", "Shift+V"), out vkTalk, out talkShift, out talkCtrl, out talkAlt, out tn);
            if (vkTalk == 0) vkTalk = (int)Keys.Menu;
            ParseCombo(cfg.Get("TeclaLimpiar", "Shift+G"), out vkClear, out clearSh, out clearCt, out clearAl, out tn);
            SetTypeKey(cfg.Get("TeclaEscribir", "Shift+T"));
            ParseCombo(cfg.Get("TeclaNombres", "Shift+M"), out vkNames, out namesSh, out namesCt, out namesAl, out tn);
            ParseCombo(cfg.Get("TeclaAyuda", "Shift+H"), out vkHelp, out helpSh, out helpCt, out helpAl, out tn);
            showNames = Regex.IsMatch(cfg.Get("Nombres", "no"), "^(si|sí|s|yes|true|1)$", RegexOptions.IgnoreCase);
            port = cfg.GetInt("Puerto", 8765);

            try { cat = Catalog.Load(Path.Combine(dir, "datos")); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "DBD Overlay"); Environment.Exit(1); }
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
                Feedback(voice ? "voz encendida: cargando Whisper…" : "voz encendida, pero esta PC no tiene Whisper (instalalo desde el menú)", Color.FromArgb(150, 220, 150), 5000);
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
                Feedback("voz apagada: Whisper cerrado y micrófono liberado (" + typeKeyName + " para escribir)", Color.Silver, 5000);
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
                whisperProc = p;
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
            UpdateVisibility(cursor);
            SetMoveHook(cursor && Visible);
            bool ro = Visible && cursor && roomRect.Contains(new Point(ci.x - Left, ci.y - Top));
            if (ro != roomHover) { roomHover = ro; Relayout(); }
            if (Visible && !helpShown && cursor)
            {
                var p = new Point(ci.x - Left, ci.y - Top);
                foreach (var it in items)
                    if (it.E != null && !string.IsNullOrEmpty(DInfo(it.E)) && it.R.Contains(p)) { over = it.E; rr = it.R; }
            }
            if (over != hoverE) { hoverE = over; hoverSince = Environment.TickCount; if (over == null && tip != null) tip.Hide(); return; }
            if (over == null || Environment.TickCount - hoverSince < 250) return;
            if (tip != null && tip.Visible && tip.Current == over) return;
            if (tip == null) tip = new HoverTip(new Font("Segoe UI", fItem.Size, FontStyle.Bold), fSmall, GetIcon);
            string title = DName(over);
            string sub = over.K == Kind.Perk ? "Perk" + (!string.IsNullOrEmpty(over.Owner) ? " de " + over.Owner : " general")
                       : over.K == Kind.Offering ? "Ofrenda" : "Addon" + (over.Killer != null && over.Killer.Length > 0 ? " · " + over.Killer : "");
            if (st.IsDoubt(over)) sub += " · sospecha (sin confirmar)";
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
                    if (manual && what == null) Feedback(err != null ? "No pude buscar la actualización (" + err + ")" : "Ya tenés la última versión (" + Updater.AppVersion + ")", err != null ? Color.FromArgb(240, 170, 80) : Color.FromArgb(150, 220, 150), 5000);
                    if (what == null) return;
                    DebugEvent("actualización instalada (" + what + "): reinicio");
                    Feedback("DBD Overlay actualizado a la " + what + " · reiniciando…", Color.FromArgb(150, 220, 150), 4000);
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
            SetTypeKey(cfg.Get("TeclaEscribir", "Shift+T"));
            st.ClearKeyName = cfg.Get("TeclaLimpiar", "Shift+G");
        }

        // accion -> (clave de config, valor por defecto, se puede dejar sin tecla)
        static readonly Tuple<string, string, string, bool>[] KeyActions = {
            Tuple.Create("Hablar (mantener)", "Tecla", "Shift+V", false),
            Tuple.Create("Escribir", "TeclaEscribir", "Shift+T", true),
            Tuple.Create("Borrar lista (mantener 1 s)", "TeclaLimpiar", "Shift+G", true),
            Tuple.Create("Ayuda (mantener)", "TeclaAyuda", "Shift+H", true),
            Tuple.Create("Mostrar nombres", "TeclaNombres", "Shift+M", true),
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
                    { Feedback(combo + " ya es la tecla de \"" + o.Item1 + "\"", Color.FromArgb(240, 170, 80), 5000); return; }
                SetCfg(act.Item2, combo);
                LoadKeys();
                Feedback(act.Item1 + ": " + (combo.Length > 0 ? combo : "sin tecla"), Color.FromArgb(150, 220, 150), 4000);
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
                FormBorderStyle = FormBorderStyle.FixedToolWindow; Text = "Tecla: " + action; StartPosition = FormStartPosition.CenterScreen;
                TopMost = true; ShowInTaskbar = false; KeyPreview = true; Width = 440; Height = 170;
                BackColor = Color.FromArgb(18, 18, 24);
                lbl = new Label { Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                                  Text = current.Length > 0 ? current : "(sin tecla)" };
                sub = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopCenter, ForeColor = Color.FromArgb(150, 150, 165), Font = new Font("Segoe UI", 9f),
                                  Text = "Apretá la combinación nueva (por ejemplo Shift+V o un botón lateral del mouse).\nEsc: cancelar" + (canBeNone ? " · Supr: sin tecla" : "") };
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
            if (feedback.StartsWith("⌨ escribiendo")) { feedback = ""; Relayout(); }
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
            Feedback("⌨ escribiendo… las teclas van al cuadro · Esc para cerrar", Color.FromArgb(240, 200, 110), int.MaxValue / 2);
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
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "ocultar", "sacar", "cerrar", "quitar", "hide"))) ml.Add(new Entry { K = Kind.Offering, Name = "Ocultar mapa", Action = "mapa-ocultar", ActionTag = mapShown });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "grande", "agrandar", "mas", "zoom", "tamano"))) ml.Add(new Entry { K = Kind.Offering, Name = "Mapa más grande", Action = "mapa-mas", ActionTag = MapSize + " px" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "chico", "achicar", "menos", "pequeno", "tamano"))) ml.Add(new Entry { K = Kind.Offering, Name = "Mapa más chico", Action = "mapa-menos", ActionTag = MapSize + " px" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "transparente", "opacidad", "clarito", "invisible"))) ml.Add(new Entry { K = Kind.Offering, Name = "Mapa más transparente", Action = "mapa-transp", ActionTag = MapOpacity + " %" });
                if (mapShown != null && (mr.Length == 0 || Kw(mr, "opaco", "solido", "opacidad", "visible"))) ml.Add(new Entry { K = Kind.Offering, Name = "Mapa más opaco", Action = "mapa-opaco", ActionTag = MapOpacity + " %" });
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
                l.Add(new Entry { K = Kind.Offering, Name = "Entrar a la sala " + code, Action = "entrar:" + code, ActionTag = ManualRoom ? "sale de la actual" : "" });
                return l;
            }
            bool wantExit = Kw(n, "salir", "exit", "leave", "dejar", "irme"), wantNew = Kw(n, "nueva", "crear", "new");
            bool anyRoom = Kw(n, "sala", "room");
            bool salir = ManualRoom && (wantExit || (anyRoom && !wantNew));
            bool nueva = wantNew || (anyRoom && !wantExit);
            if (salir) l.Add(new Entry { K = Kind.Offering, Name = "Salir de la sala " + cfg.Get("Sala", ""), Action = "salir", ActionTag = RoomCount > 0 ? RoomCount + "/" + MaxRoom + " adentro" : "" });
            if (nueva) l.Add(new Entry { K = Kind.Offering, Name = "Crear sala nueva", Action = "nueva", ActionTag = "copia el código para el chat" });
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
                case Kind.Perk: return !string.IsNullOrEmpty(e.Owner) ? "de " + e.Owner : "general";
                case Kind.Offering: return "";
                default: return (e.Killer ?? "") + (e.Rarity != null && ColorNameOf(e.Rarity).Length > 0 ? " · " + ColorNameOf(e.Rarity) : "");
            }
        }
        static string ColorNameOf(string rar)
        {
            switch (rar) { case "visceral": return "rojo"; case "veryrare": return "violeta"; case "rare": return "verde"; case "uncommon": return "amarillo"; case "common": return "marrón"; default: return ""; }
        }

        static readonly Regex TypedCmdRx = new Regex(@"^(?:tiene|tenia|quitar?|sacar?|borrar?|creo|probablemente|capaz|el killer|killer|ofrenda|no tiene|confirmad|descartad|el (?:uno|dos|tres|cuatro|cinco|[1-5]))\b");

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
            Log("[texto] " + said, what);
            bool ok = !what.StartsWith("no ");
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Feedback("⌨ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }

        // ---- quitar / cambiar / duda desde el cuadro
        static readonly Regex BoxModeRx = new Regex(@"^(quitar?|sacar?|borrar?|cambiar?|reemplazar?|duda|dudo|sospecha|seguro|confirmar?)\b\s*(.*)$");
        public string BoxMode(string text)
        {
            var m = BoxModeRx.Match(Txt.Norm(text ?? ""));
            if (!m.Success) return "";
            string w = m.Groups[1].Value;
            return w.StartsWith("camb") || w.StartsWith("reemp") ? "cambiar" : w.StartsWith("dud") || w == "sospecha" || w == "seguro" || w.StartsWith("confirm") ? "duda" : "quitar";
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

        public void BoxDo(string said, Func<string> act)
        {
            var before = st.Snapshot();
            string what = act();
            Heard("texto", said, what, false);
            Log("[texto] " + said, what);
            bool ok = !what.StartsWith("no ");
            if (ok) { st.Save(Path.Combine(dir, "estado.txt")); PublishDiff(before); }
            Feedback("⌨ " + what, ok ? Color.FromArgb(150, 220, 150) : Color.FromArgb(240, 170, 80), 5000);
            Relayout();
        }
        public void BoxRemove(Entry e) { BoxDo("quitar " + e.Name, () => st.RemovePicked(e)); }
        public void BoxReplace(Entry old, Entry neu, bool doubt) { BoxDo("cambiar " + old.Name + " por " + neu.Name, () => st.ReplacePicked(old, neu, doubt)); }
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
                if (ctrl) return;
                foreach (char c in text) if (!char.IsControl(c)) SetText(tb.Text + c);
            }
            string HintText { get { return "Enter: anotar · \"noed?\" o Shift+Enter: sospecha · ↑↓ elegir · Esc: cerrar" + (ov.HelpKeyName.Length > 0 ? " · " + ov.HelpKeyName + ": ayuda" : "") + "\n" + HintRest; } }
            const string HintRest = "\"addon\" · \"addon 15 lento\" · \"perk aura gancho\" · \"sala\" · \"quitar …\" · \"cambiar …\" · \"duda …\"";
            public void FocusText() { tb.Focus(); }
            public void SetText(string s) { tb.Text = s; tb.SelectionStart = s.Length; }

            void Refresh2()
            {
                string why = null;
                if (replaceNew != null)
                {
                    sugg = ov.CurrentMatching(tb.Text, replaceNew.K);
                    why = "Ya está lleno: ¿cuál sacás para poner " + replaceNew.Name + "?  (Enter: ese · Esc: nada)";
                }
                else if (replaceTarget != null)
                {
                    var rt = replaceTarget;
                    sugg = Txt.Norm(tb.Text).Length == 0 ? new List<Entry>() : ov.Suggest(tb.Text).Where(x => x.K == rt.K && x != rt && x.Action == null).ToList();
                    why = "Cambiar " + rt.Name + " por…  (escribí el nuevo · Shift+Enter: como sospecha)";
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
                    if (sugg.Count > 0) why = qt + " · ↓ y Enter para anotar (o escribí)";
                }
                else
                {
                    sugg = ov.Suggest(tb.Text);
                    string mode = ov.BoxMode(tb.Text);
                    if (mode == "quitar") why = "Quitar: elegí cuál (Enter)";
                    else if (mode == "cambiar") why = "Cambiar: elegí cuál sale (Enter) y después escribí el nuevo";
                    else if (mode == "duda") why = "Duda: Enter alterna entre seguro y sospecha (?)";
                    if (mode.Length > 0 && sugg.Count == 0) why = "No hay nada anotado que coincida";
                }
                confirmKiller = null;
                lb.Items.Clear();
                foreach (var e in sugg) lb.Items.Add(e);
                if (sugg.Count > 0 && Txt.Norm(tb.Text).Length > 0) lb.SelectedIndex = 0; // cuadro vacio: nada elegido hasta bajar con flecha
                byArrows = false;
                lb.Height = Math.Min(sugg.Count, 10) * lb.ItemHeight; // con mas de 10 aparece la barra para bajar
                if (sugg.Count > 0) lb.TopIndex = 0;
                bool noKiller = Regex.IsMatch(Txt.Norm(tb.Text), @"^(?:addons?|add ons?|adon|accesorios?)$") && !ov.HasKiller;
                hint.Text = noKiller ? "Sin killer anotado: escribí algo de la descripción (\"addon 15 lento\") o anotá el killer primero." : why != null ? why + "\n" + HintText.Split('\n')[0] : HintText;
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
                    case Kind.Perk: kind = "PERK"; pill = Color.FromArgb(70, 80, 120); break;
                    case Kind.Offering: kind = "OFRENDA"; pill = Color.FromArgb(140, 110, 40); break;
                    default: { var rc = RarityColor(en.Rarity); kind = "ADDON"; pill = Color.FromArgb(rc.R / 2, rc.G / 2, rc.B / 2); break; }
                }
                var pr = new Rectangle(x, e.Bounds.Y + 4, PillW, e.Bounds.Height - 8);
                using (var b = new SolidBrush(pill)) e.Graphics.FillRectangle(b, pr);
                TextRenderer.DrawText(e.Graphics, kind, PillFont, pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += PillW + 8;
                Color c = en.K == Kind.Killer ? Color.FromArgb(255, 120, 120) : en.K == Kind.Perk ? Color.WhiteSmoke : RarityColor(en.Rarity);
                string name = ov.DName(en);
                using (var nf = en.K == Kind.Killer ? new Font(lb.Font, FontStyle.Bold) : null)
                {
                    var f0 = nf ?? lb.Font;
                    TextRenderer.DrawText(e.Graphics, name, f0, new Point(x, e.Bounds.Y + 3), c, TextFormatFlags.NoPrefix);
                    x += TextRenderer.MeasureText(name, f0).Width;
                }
                using (var f = new Font("Segoe UI", 8.5f))
                    TextRenderer.DrawText(e.Graphics, ov.SuggestTag(en) + (ov.IsDoubt(en) ? "  · sospecha (?)" : ""), f, new Point(x + 10, e.Bounds.Y + 6), Color.FromArgb(130, 130, 145), TextFormatFlags.NoPrefix);
            }
            static readonly Font PillFont = new Font("Segoe UI", 7f, FontStyle.Bold);
            const int PillW = 58;

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
                    hint.Text = "¿Cambiar " + cur.Name + " por " + picked.Name + "? Se borran los addons.\nEnter: sí, cambiar · Esc: no";
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
            if (what.StartsWith("no ") || what.Contains("ya estaba")) { Heard("sin tecla", raw, what, false); return; } // sin anotar: solo se ve si "mostrar lo que entiende" esta prendido
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
                    Feedback((joined.Count > 0 ? "👥 " + string.Join(", ", joined) + " en el grupo" : "") +
                             (left.Count > 0 ? (joined.Count > 0 ? " · " : "👥 ") + string.Join(", ", left) + " salió" : "") +
                             (partyCount == 0 ? " · solo vos" : ""), Color.FromArgb(130, 200, 140), 5000);
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
            return "alguien del grupo";
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
                    Feedback("La sala está llena (" + MaxRoom + "/" + MaxRoom + "): no pudiste entrar", Color.FromArgb(240, 170, 80), 8000);
                    return true;
                }
                if (!me && recent && pos < MaxRoom) Feedback(nm + " entró a la sala (" + RoomCount + "/" + MaxRoom + ")", Color.FromArgb(130, 190, 255), 5000);
            }
            else if (leave && roomMembers.Remove(who))
            {
                if (!me && recent) Feedback(nm + " salió de la sala (" + RoomCount + "/" + MaxRoom + ")", Color.Silver, 5000);
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
            object mp;
            if (op.TryGetValue("map", out mp))
            {
                // mapa elegido por alguien de la sala (los de hace mas de 10 min, del historial, no)
                if (!mine && op.TryGetValue("t", out rt0) && UnixNow() - Convert.ToInt64(rt0) < 600)
                {
                    string mn = (mp ?? "").ToString();
                    if (mn.Length == 0) HideMap(false); else { ShowMap(mn, false); Feedback(SenderName(op) + ": mapa " + mn, Color.FromArgb(130, 190, 255), 5000); }
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
            if (room == null || before == null) return;
            var op = st.Diff(before);
            if (op == null) return;
            room.Publish(Stamp(op));
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
                Feedback("lista igualada con " + SenderName(op), Color.FromArgb(130, 190, 255), 4000);
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
            AnnounceLeave();
            SetCfg("Sala", code);
            StopSteam();
            StartRoom();
            PublishAll();
            try { Clipboard.SetText("sala " + code); } catch { }
            MessageBox.Show("Sala creada. Código: " + code + "\n\n(ya está copiado como \"sala " + code + "\": pegalo en el chat)\n\n" +
                            "Los demás: Shift+T y escriben \"sala " + code + "\" (o clic derecho en el ícono \"D\" → Sala compartida → Unirse con código…).\n\n" +
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
            JoinRoomCode(code);
        }

        // ---- sala desde el cuadro de escribir (sin salir del juego): "sala nueva", "sala <codigo>", "sala salir"
        public string BoxRoomHint(string text)
        {
            var m = Regex.Match(Txt.Norm(text ?? ""), @"^sala\b\s*(.*)$");
            if (!m.Success) return null;
            string r = m.Groups[1].Value.Trim();
            if (r.Length == 0) return ManualRoom ? "Estás en la sala " + cfg.Get("Sala", "") + " · \"sala salir\" · \"sala nueva\"" : "\"sala nueva\": crear · \"sala CÓDIGO\": entrar · \"sala salir\"";
            if (Regex.IsMatch(r, @"^(nueva|crear|crea|new)$")) return "Enter: crear sala (el código queda copiado para el chat)";
            if (Regex.IsMatch(r, @"^(salir|dejar|leave)$")) return "Enter: salir de la sala con código";
            string code = Room.CleanCode(r);
            return code.Length >= 6 ? "Enter: entrar a la sala " + code : "Código incompleto";
        }
        public bool BoxRoom(string text)
        {
            var m = Regex.Match(Txt.Norm(text ?? ""), @"^sala\b\s*(.*)$");
            if (!m.Success) return false;
            string r = m.Groups[1].Value.Trim();
            if (MyName.Length == 0) SetCfg("Nombre", "Survivor " + new Random().Next(10, 100)); // sin ventana para preguntar (se cambia desde el menu)
            if (Regex.IsMatch(r, @"^(nueva|crear|crea|new)$"))
            {
                string code = Room.NewCode();
                AnnounceLeave();
                SetCfg("Sala", code);
                StopSteam(); StartRoom(); PublishAll();
                try { Clipboard.SetText("sala " + code); } catch { }
                Feedback("sala creada: " + code + " · copiado \"sala " + code + "\", pegalo en el chat (Ctrl+V)", Color.FromArgb(130, 190, 255), 15000);
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
            Feedback("entraste a la sala " + code, Color.FromArgb(130, 190, 255), 5000);
        }

        void LeaveRoom()
        {
            AnnounceLeave();
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

        public bool previewRoomLine; // solo para probar en la vista
        bool ShowRoomLine { get { return !Regex.IsMatch(cfg.Get("MostrarSala", "si"), "^(no|n|false|0)$", RegexOptions.IgnoreCase); } }
        ToolStripMenuItem roomLineItem;
        bool roomHover; Rectangle roomRect; // icono de sala: se expande con el mouse encima
        string RoomLine()
        {
            if (!ShowRoomLine || (Preview && !previewRoomLine) || (room == null && !Preview)) return null;
            if (!ManualRoom && partyCount <= 0 && !Preview) return null;
            if (!roomHover) return "👥";
            string conn = roomStatus.StartsWith("conectad") ? "" : " · conectando…";
            if (ManualRoom) return "👥 sala " + cfg.Get("Sala", "") + (roomMembers.Count > 0 ? " · " + RoomCount + "/" + MaxRoom : "") + conn;
            if (partyCount > 0) return "👥 grupo de Steam +" + partyCount + conn;
            return null;
        }

        void UpdateRoomMenu()
        {
            if (roomLabel == null) return;
            roomLabel.Text = RoomText();
            autoItem.Checked = AutoRoom;
            roomCopy.Enabled = roomLeave.Enabled = ManualRoom;
            string t = "DBD Overlay v" + Updater.AppVersion + " · " + RoomText();
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
            mapWin.Opacity = MapOpacity / 100.0;
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
            Tuple.Create("arriba-derecha", "Arriba a la derecha"), Tuple.Create("arriba-izquierda", "Arriba a la izquierda"),
            Tuple.Create("abajo-derecha", "Abajo a la derecha"), Tuple.Create("abajo-izquierda", "Abajo a la izquierda") };

        // valores de fabrica de todo lo que es apariencia (las teclas, la sala y la voz no se tocan)
        static readonly Tuple<string, string>[] LookDefaults = {
            Tuple.Create("Posicion", "arriba-derecha"), Tuple.Create("MargenX", "30"), Tuple.Create("MargenY", "140"),
            Tuple.Create("Escala", "100"), Tuple.Create("TamanoPerk", "72"), Tuple.Create("TamanoLetra", "14"),
            Tuple.Create("TamanoKiller", "12"), Tuple.Create("Opacidad", "0.85"), Tuple.Create("TamanoMapa", "220"),
            Tuple.Create("OpacidadMapa", "90"), Tuple.Create("MapaX", "20"), Tuple.Create("MapaY", "20") };

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
            static readonly string[] Pages = { "Lista", "Mapa", "Teclas", "Sala", "Voz", "General" };
            const int X = 24, W = 420;
            static Color Bg { get { return Dbd.Bg; } }
            static Color Fg { get { return Dbd.Fg; } }
            static Color Dim { get { return Dbd.Dim; } }

            [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

            public SettingsWindow(Overlay o, string page)
            {
                ov = o;
                Text = "DBD Overlay · Configuración";
                FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(640, 580);
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
                    TextRenderer.DrawText(g, "DBD", Dbd.Head(22f), new Point(18, 16), Dbd.AccHi, TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, "OVERLAY", Dbd.Head(11f), new Point(21, 50), Dbd.Fg, TextFormatFlags.NoPrefix);
                    using (var br = new SolidBrush(Dbd.Acc)) g.FillRectangle(br, 21, 74, 34, 2);
                    TextRenderer.DrawText(g, "v" + Updater.AppVersion, Dbd.Cond(9f), new Point(21, nav.Height - 28), Dbd.Dim, TextFormatFlags.NoPrefix);
                    using (var pen = new Pen(Dbd.Line)) g.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
                };
                int ny = 96;
                foreach (var p in Pages)
                {
                    var l = new DNav { Text = p, Bounds = new Rectangle(0, ny, 159, 40), Font = Dbd.Head(11.5f), BackColor = Dbd.Side };
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

            void Refresh2() { refreshing = true; try { foreach (var r in refreshers) r(); } finally { refreshing = false; } }

            public void ShowPage(string page)
            {
                if (Array.IndexOf(Pages, page) < 0) page = "Lista";
                cur = page;
                foreach (var l in navItems) { l.Selected = l.Text == page; l.Invalidate(); }
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
                Title("Lista de perks");
                var sample = Buttons(B("Ver una lista de ejemplo", () => { if (ov.SampleOn) ov.EndSample(); else ov.StartSample(); }, () => ov.SampleOn || !ov.HasAnything()))[0];
                refreshers.Add(() => sample.Text = (ov.SampleOn ? "Quitar la lista de ejemplo" : "Ver una lista de ejemplo").ToUpperInvariant());
                Note("Con la lista vacía podés poner una de ejemplo para ver cómo queda cada cambio. Se quita sola al cerrar esta ventana.");
                Choice("Esquina de la pantalla", Corners.Select(c => c.Item2).ToArray(),
                    () => { string p = C.Get("Posicion", "arriba-derecha").ToLowerInvariant(); return (p.Contains("abajo") ? 2 : 0) + (p.Contains("izq") ? 1 : 0); },
                    i => { ov.SetCfg("Posicion", Corners[i].Item1); if (ov.tip != null) ov.tip.Hide(); ov.Relayout(); });
                Slider("Distancia al borde (de costado)", 0, scr.Width / 2, () => C.GetInt("MargenX", 30), v => { ov.Live("MargenX", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider("Distancia al borde (de arriba o abajo)", 0, scr.Height / 2, () => C.GetInt("MargenY", 140), v => { ov.Live("MargenY", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider("Escala general", 50, 200, () => C.GetInt("Escala", 100), v => { ov.Live("Escala", v.ToString()); Look(); }, v => v + " %");
                Slider("Tamaño de los íconos", 24, 120, () => C.GetInt("TamanoPerk", 72), v => { ov.Live("TamanoPerk", v.ToString()); ov.Relayout(); }, v => v + " px");
                Slider("Tamaño de la letra", 8, 28, () => C.GetInt("TamanoLetra", 14), v => { ov.Live("TamanoLetra", v.ToString()); Look(); }, v => v.ToString());
                Slider("Opacidad", 30, 100, () => (int)Math.Round(C.GetD("Opacidad", 0.85) * 100),
                    v => { ov.Live("Opacidad", (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture)); ov.Opacity = v / 100.0; }, v => v + " %");
                Check("Mostrar los nombres de perks y addons", () => ov.showNames, v => ov.SetNames(v));
                Check("Mostrar el nombre del killer", () => ov.KillerOn, v => { ov.SetCfg("MostrarKiller", v ? "si" : "no"); ov.Relayout(); });
                Slider("Tamaño del nombre del killer", 6, 22, () => C.GetInt("TamanoKiller", 12), v => { ov.Live("TamanoKiller", v.ToString()); ov.killerFont = null; ov.Relayout(); }, v => v.ToString());
                Check("Mostrar solo con el menú de DBD abierto (ESC)", () => ov.MenuOnly, v => ov.SetCfg("Visibilidad", v ? "menu" : "siempre"));
                Note("Mientras jugás la lista se oculta y aparece al abrir el menú ESC, al escribir con Shift+T o cuando hay un mensaje.");
            }

            void PageMap()
            {
                var scr = Screen.PrimaryScreen.Bounds;
                Title("Mapa");
                var show = Buttons(B("Ver un mapa de ejemplo", () =>
                {
                    if (ov.mapShown != null) { ov.HideMap(false); return; }
                    var m = ov.cat.Maps.FirstOrDefault(x => ov.cat.MapFiles.ContainsKey(x.Name));
                    if (m != null) ov.ShowMap(m.Name, false);
                }, () => ov.cat.MapFiles.Count > 0))[0];
                refreshers.Add(() => show.Text = (ov.mapShown != null ? "Ocultar el mapa" : "Ver un mapa de ejemplo").ToUpperInvariant());
                Note("El mapa se elige escribiendo su nombre en el cuadro de Shift+T (por ejemplo \"mapa coal\"). Cuando estás en sala, lo ven todos.");
                Slider("Tamaño", 160, 700, () => ov.MapSize, v => { ov.Live("TamanoMapa", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Slider("Opacidad", 15, 100, () => ov.MapOpacity, v => { ov.Live("OpacidadMapa", v.ToString()); if (ov.mapWin != null) ov.mapWin.Opacity = v / 100.0; }, v => v + " %");
                Slider("Posición desde la izquierda", 0, Math.Max(100, scr.Width - 160), () => C.GetInt("MapaX", 20), v => { ov.Live("MapaX", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Slider("Posición desde arriba", 0, Math.Max(100, scr.Height - 160), () => C.GetInt("MapaY", 20), v => { ov.Live("MapaY", v.ToString()); ov.RefreshMap(); }, v => v + " px");
                Check("Mostrar solo con el menú de DBD abierto (ESC)", () => ov.MapMenuOnly, v => { ov.SetCfg("MapaVisibilidad", v ? "menu" : "siempre"); ov.UpdateMapVisibility(ov.cursorShown); });
                Note("Planos con callouts de Lethia (hens333.com/callouts), versión de Zexov sobre el original de Broosley y Evo.");
            }

            void PageKeys()
            {
                Title("Teclas");
                foreach (var act in KeyActions)
                {
                    var a = act;
                    body.Controls.Add(new Label { Text = a.Item1, AutoSize = true, Location = new Point(X, y + 6), ForeColor = Fg });
                    var v = new Label { AutoSize = false, Bounds = new Rectangle(X + 215, y + 4, 115, 22), ForeColor = Dbd.AccHi, Font = Dbd.Head(11f), UseMnemonic = false };
                    refreshers.Add(() => { string k = C.Get(a.Item2, a.Item3); v.Text = k.Length > 0 ? k : "sin tecla"; });
                    body.Controls.Add(v);
                    Btn("Cambiar…", X + 330, () => ov.AskKey(a));
                    y += 40;
                }
                y += 4;
                Check("Las teclas solo funcionan con DBD al frente", () => ov.KeysOnlyInDbd, v => ov.SetCfg("TeclasSoloEnDBD", v ? "si" : "no"));
                Note("Así, escribiendo en Discord o en el navegador, Shift+T o Shift+G no hacen nada. Al cambiar una tecla podés usar combinaciones (Shift, Ctrl, Alt) o un botón del mouse.");
            }

            void PageRoom()
            {
                Title("Sala compartida");
                Value(() => ov.RoomText());
                Note("En sala, lo que anota uno lo ven todos.");
                Check("Automática con mi grupo de Steam", () => ov.AutoRoom, v => ov.SetAuto(v));
                Note("Con DBD abierto, todos los del mismo grupo de Steam que tengan la app quedan juntos, sin hacer nada.");
                Title("Sala con código");
                Note("Para jugar con gente que no está en tu grupo de Steam (hasta 4 por sala). También se maneja desde Shift+T escribiendo \"sala nueva\", \"sala CÓDIGO\" o \"salir\".");
                Value(() => ov.ManualRoom ? "Código: " + ov.cfg.Get("Sala", "") : "No estás en una sala con código");
                Buttons(B("Crear sala", () => ov.CreateRoom()), B("Unirse…", () => ov.JoinRoom()),
                        B("Copiar código", () => { try { Clipboard.SetText(ov.cfg.Get("Sala", "")); } catch { } }, () => ov.ManualRoom),
                        B("Salir", () => ov.LeaveRoom(), () => ov.ManualRoom));
                Value(() => "Tu nombre: " + (ov.MyName.Length > 0 ? ov.MyName : "(sin nombre)"));
                Buttons(B("Cambiar mi nombre…", () => ov.AskName()));
                Check("Mostrar el ícono de sala en la lista", () => ov.ShowRoomLine, v => { ov.SetCfg("MostrarSala", v ? "si" : "no"); if (ov.roomLineItem != null) ov.roomLineItem.Checked = v; ov.Relayout(); });
            }

            void PageVoice()
            {
                Title("Voz");
                Note("Anotar hablando con Whisper (corre en tu PC, sin internet). Usa bastante procesador mientras entiende una frase: en PCs con pocos recursos conviene dejarla apagada y anotar con Shift+T.");
                Check("Reconocimiento de voz", () => ov.VoiceOn, v => ov.SetVoice(v));
                var devs = new List<string> { "Predeterminado de Windows" };
                try { devs.AddRange(Mic.Devices()); } catch { }
                Choice("Micrófono", devs.ToArray(),
                    () => { string m = C.Get("Microfono", ""); int i = devs.FindIndex(d => d.Equals(m, StringComparison.OrdinalIgnoreCase)); return m.Length == 0 ? 0 : i; },
                    i => { ov.SetCfg("Microfono", i == 0 ? "" : devs[i]); if (ov.mic != null) ov.StartMic(); });
                var hands = Check("Hablar sin tecla (prueba)", () => ov.HandsOn, v => ov.SetHands(v));
                var listen = Check("Escuchar Discord (prueba)", () => ov.ListenOn, v => ov.SetListen(v));
                var heard = Check("Mostrar lo que entiende Whisper", () => ov.ShowHeard, v => ov.SetCfg("VerTranscripcion", v ? "si" : "no"));
                Func<bool> inst = () => ov.voice || (ov.FindWhisperExe() != null && ov.FindModel() != null);
                refreshers.Add(() => { bool v = ov.VoiceOn; hands.Enabled = listen.Enabled = heard.Enabled = v; });
                var bs = Buttons(B("Ver todo lo que escuchó…", () =>
                        {
                            string path = Path.Combine(ov.dir, "logs", "escuchado.txt");
                            try { if (!File.Exists(path)) File.WriteAllText(path, "", Encoding.UTF8); Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
                        }), B("Instalar Whisper…", () => ov.InstallVoice(), () => !inst() && !ov.installing));
                refreshers.Add(() => bs[1].Text = (inst() ? "Whisper instalado ✓" : ov.installing ? "Instalando Whisper…" : "Instalar Whisper…").ToUpperInvariant());
            }

            void PageGeneral()
            {
                Title("Idioma");
                var langs = new[] { "Inglés", "Español" };
                Choice("Nombres", langs, () => C.Get("IdiomaNombres", "en").ToLowerInvariant().StartsWith("es") ? 1 : 0,
                    i => { ov.SetCfg("IdiomaNombres", i == 1 ? "es" : "en"); if (ov.tip != null) { ov.tip.Hide(); ov.hoverE = null; } ov.Relayout(); });
                Choice("Descripciones", langs, () => C.Get("IdiomaDescripciones", "es").ToLowerInvariant().StartsWith("es") ? 1 : 0,
                    i => { ov.SetCfg("IdiomaDescripciones", i == 1 ? "es" : "en"); if (ov.tip != null) { ov.tip.Hide(); ov.hoverE = null; } ov.Relayout(); });
                Title("App");
                Value(() => "Versión " + Updater.AppVersion);
                Buttons(B("Buscar actualización", () => { if (ov.updating) return; ov.Feedback("Buscando actualización…", Color.FromArgb(200, 200, 210), 4000); ov.CheckUpdate(true); }),
                        B("Abrir carpeta", () => { try { Process.Start("explorer.exe", "\"" + ov.dir + "\""); } catch { } }));
                Buttons(B("Editar apodos", () => { try { ov.st.ReloadAliases(); Process.Start("notepad.exe", "\"" + Path.Combine(ov.dir, "apodos.txt") + "\""); } catch { } }),
                        B("Editar config.ini (avanzado)", () => { ov.FlushCfg(); try { Process.Start("notepad.exe", "\"" + Path.Combine(ov.dir, "config.ini") + "\""); } catch { } }));
                Note("Los apodos son nombres alternativos para perks, addons o killers (por ejemplo \"ruina\" para Hex: Ruin).");
                Title("Apariencia");
                Buttons(B("Volver la apariencia a la de fábrica", () =>
                {
                    if (MessageBox.Show(this, "¿Volver el tamaño, la posición y la opacidad de la lista y del mapa a los valores de fábrica?\n(Las teclas, la sala y la voz no se tocan.)", "DBD Overlay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var d in LookDefaults) ov.SetCfg(d.Item1, d.Item2);
                    ov.Opacity = 0.85; ov.MakeFonts(); ov.Relayout(); ov.RefreshMap();
                    ov.Feedback("apariencia de fábrica", Color.Silver, 3000);
                }));
            }
        }

        void SetupTray()
        {
            trayRed = TrayIcon(Color.FromArgb(190, 22, 34));
            trayGreen = TrayIcon(Color.FromArgb(40, 150, 70));
            tray = new NotifyIcon { Icon = trayRed, Text = "DBD Overlay", Visible = true };
            // doble clic en el icono: configuracion
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(null); };
            var menu = new ContextMenuStrip();
            // arriba de todo, la version (para saber si ya se actualizo)
            menu.Items.Add(new ToolStripMenuItem("DBD Overlay · versión " + Updater.AppVersion) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var cfgItem = new ToolStripMenuItem("Configuración…", null, (s, e) => OpenSettings(null));
            cfgItem.Font = new Font(cfgItem.Font, FontStyle.Bold);
            menu.Items.Add(cfgItem);
            var typeItem = new ToolStripMenuItem("Escribir…");
            typeItem.Click += (s, e) => OpenTypeBox();
            menu.Items.Add(typeItem);
            menu.Items.Add("Borrar lista", null, (s, e) => ClearWithUndo());
            // lo de siempre de la sala queda a mano; el resto esta en Configuracion > Sala
            var roomMenu = new ToolStripMenuItem("Sala compartida");
            roomLabel = new ToolStripMenuItem("Sin sala") { Enabled = false };
            roomMenu.DropDownItems.Add(roomLabel);
            roomMenu.DropDownItems.Add("-");
            autoItem = new ToolStripMenuItem("Automática con mi grupo de Steam") { Checked = AutoRoom };
            autoItem.Click += (s, e) => SetAuto(!AutoRoom);
            roomMenu.DropDownItems.Add(autoItem);
            roomMenu.DropDownItems.Add("-");
            roomMenu.DropDownItems.Add("Crear sala con código", null, (s, e) => CreateRoom());
            roomMenu.DropDownItems.Add("Unirse con código…", null, (s, e) => JoinRoom());
            roomCopy = new ToolStripMenuItem("Copiar código", null, (s, e) =>
            {
                try { Clipboard.SetText(cfg.Get("Sala", "")); Feedback("código copiado: " + cfg.Get("Sala", ""), Color.Silver, 3000); } catch { }
            });
            roomMenu.DropDownItems.Add(roomCopy);
            roomLeave = new ToolStripMenuItem("Salir de la sala con código", null, (s, e) => LeaveRoom());
            roomMenu.DropDownItems.Add(roomLeave);
            menu.Items.Add(roomMenu);
            // estos ya no estan en el menu (estan en la ventana), pero otras partes los actualizan
            roomLineItem = new ToolStripMenuItem("Mostrar en la lista si estoy en sala") { Checked = ShowRoomLine };
            namesItem = new ToolStripMenuItem("Mostrar nombres") { Checked = showNames };
            listenItem = new ToolStripMenuItem("Escuchar Discord (prueba)");
            handsItem = new ToolStripMenuItem("Hablar sin tecla (prueba)");
            menu.Opening += (s, e) => { typeItem.Text = "Escribir… (" + typeKeyName + ")"; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Buscar actualización", null, (s, e) => { if (updating) return; Feedback("Buscando actualización…", Color.FromArgb(200, 200, 210), 4000); CheckUpdate(true); });
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
            bool fast = !watchOk || mouseKeys || typePending || talking || releaseAt >= 0 || clearDown || helpShown || namesDown || (typeKeyDown && !boxOpen);
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

        bool IsOurVk(int v) { return v != 0 && (v == vkType || v == vkTalk || v == vkClear || v == vkHelp || v == vkNames); }

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
                Feedback("Micrófono: " + (mic.LastError.Length > 0 ? mic.LastError : "se cortó") + " (reintento…)", Color.OrangeRed, 6000);
                DebugEvent("micrófono: " + (mic.LastError.Length > 0 ? mic.LastError : "se cortó") + " (reintento en 3 s)");
                micRestartAt = now + 3000;
            }
            if (micRestartAt >= 0 && now - micRestartAt >= 0) { micRestartAt = -1; StartMic(); }

            if (!voice)
            {
                if (down && !talkWarned && VoiceOn) // con la voz apagada a proposito, la tecla no dice nada
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
                bool c = HotKey(vkClear) && (clearDown || ModsMatch(vkClear, clearSh, clearCt, clearAl));
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
                    UnrecordLast();
                    PublishDiff(before);
                    Changed("borrado deshecho");
                    DebugEvent("borrado deshecho con tecla");
                }
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

            if (feedback.Length > 0 && now - feedbackUntil >= 0) { feedback = ""; Relayout(); }
            if (heardLine.Length > 0 && now - heardUntil >= 0) { heardLine = ""; Relayout(); }
        }

        int clearStart, undoUntil;
        bool clearFired, clearHint;
        object undoSnap;
        string ClearKeyName { get { return cfg.Get("TeclaLimpiar", "Shift+G"); } }

        bool HasAnything() { return st.Killer != null || st.Perks.Count > 0 || st.Addons.Count > 0 || st.Offering != null; }

        void ClearWithUndo()
        {
            if (!HasAnything()) { Feedback("la lista ya está vacía", Color.Silver, 2000); return; }
            undoSnap = st.Snapshot();
            undoUntil = Environment.TickCount + 8000;
            DebugEvent("lista borrada con tecla o menú (antes de borrar)");
            RecordMatch();
            HideMap(false); // los demas lo sacan con el borrado de la lista
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
                    Heard(KeyName, text, what, false);
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
            if (heardLine.Length > 0) bottom.Add(Tuple.Create(heardLine, Color.FromArgb(160, 160, 175)));
            if (feedback.Length > 0) bottom.Add(Tuple.Create(feedback, feedbackColor));
            // siempre a la vista: si estas en una sala (linea chica, se apaga desde Sala compartida)
            string rl = RoomLine();
            if (rl != null) bottom.Add(Tuple.Create(rl, roomStatus.StartsWith("conectad") || !ManualRoom ? Color.FromArgb(110, 190, 125) : Color.FromArgb(220, 170, 90)));
            if (!any && ktext == null && bottom.Count == 0 && !helpShown)
                if (TypeKeyText.Length > 0 || voice)
                    bottom.Add(Tuple.Create((voice ? KeyName + " hablar" + (TypeKeyText.Length > 0 ? " · " : "") : "") + (TypeKeyText.Length > 0 ? TypeKeyText + " anotar" : ""), dim));

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
            Bounds = new Rectangle(wx, wy, size.Width, size.Height);
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
                if (!created) { MessageBox.Show("DBD Overlay ya está abierto (mirá el ícono rojo junto al reloj).", "DBD Overlay"); return; }
                Application.EnableVisualStyles();
                Application.Run(new Overlay(dir));
            }
        }
    }
}
