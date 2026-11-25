using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI telpon (desain Figma "UI phone screen"). Dipanggil dari DialogueManager lewat tag Ink:
///
///   #call: calling        HP muncul, nada sambung bunyi, teks "calling mobile..." beranimasi, HP bergetar
///   #call: connected      tunggu nada sambung minimal sekali, *klik* diangkat, timer 00:00 mulai jalan
///   #call_time: 01:30     loncat timer ke 01:30 (angkanya muter cepat). Bisa juga "90" (detik)
///   #call: glitch         sinyal jelek: "Poor connection", nama glitch, scanline, getar kecil
///   #call: glitch_hard    glitch parah: HP miring berkedip, bunyi glitch lebih sering
///   #call: stable         glitch berhenti, balik ke timer normal
///   #call: end            telpon ditutup normal ("Call Ended"), HP turun
///   #call: lost           sambungan putus ("Call Failed" merah), HP turun
///   #call: hide           langsung hilang tanpa animasi
///   #call_layout: center  pindah posisi HP (right / center)
///
/// Susunan object-nya dibuat otomatis lewat menu Tools > Duke Heirloom > Set Up Phone Call.
/// </summary>
public class PhoneCallUI : MonoBehaviour
{
    public enum CallState { Hidden, Calling, Connected, Glitch, Ending }

    [System.Serializable]
    public class Layout
    {
        public string id = "right";
        [Tooltip("Posisi tengah gambar HP, dihitung dari tengah layar (1920x1080).")]
        public Vector2 phonePos = new Vector2(548f, 50f);
        public float phoneScale = 0.85f;
        [Tooltip("Posisi textbox waktu telpon (anchor textbox kamu: tengah bawah).")]
        public Vector2 textboxPos = new Vector2(-270f, 150f);
        public float textboxScale = 0.7f;
    }

    public static PhoneCallUI Instance { get; private set; }

    [Header("Susunan (diisi otomatis oleh menu Set Up Phone Call)")]
    [SerializeField] RectTransform phoneRoot;
    [SerializeField] CanvasGroup phoneGroup;
    [SerializeField] RectTransform shaker;
    [SerializeField] GameObject normalStack;
    [SerializeField] Image nameImage;
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text statusGhostCyan;
    [SerializeField] TMP_Text statusGhostRed;

    [Header("Layer glitch (boleh kosong)")]
    [SerializeField] Sprite nameGlitchSprite;
    [SerializeField] Image glitchBars;
    [SerializeField] RawImage scanlines;
    [SerializeField] GameObject tiltedStack;

    [Header("Textbox yang dikecilkan waktu telpon")]
    [SerializeField] RectTransform textbox;

    [Header("Posisi")]
    [SerializeField] Layout[] layouts =
    {
        new Layout { id = "right",  phonePos = new Vector2(548f, 50f), phoneScale = 0.85f, textboxPos = new Vector2(-270f, 150f), textboxScale = 0.70f },
        new Layout { id = "center", phonePos = new Vector2(-27f, 84f), phoneScale = 0.70f, textboxPos = new Vector2(0f, 150f),    textboxScale = 0.80f },
    };

    [SerializeField] string defaultLayout = "right";
    [SerializeField] float slideDistance = 1150f;
    [SerializeField] float showDuration = 0.6f;
    [SerializeField] float hideDuration = 0.45f;

    [Header("Teks status")]
    [SerializeField] string callingText = "calling mobile";
    [SerializeField] string poorConnectionText = "Poor connection";
    [SerializeField] string endedText = "Call Ended";
    [SerializeField] string failedText = "Call Failed";
    [SerializeField] Color statusColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] Color failedColor = new Color(1f, 0.35f, 0.35f, 1f);

    [Header("Timer")]
    [Tooltip("1 = detik asli. Naikkan kalau mau timer jalan lebih cepat dari waktu asli.")]
    [SerializeField] float timerSpeed = 1f;
    [SerializeField] float rollDuration = 0.7f;

    [Header("Audio")]
    [SerializeField] AudioSource ringSource;
    [SerializeField] AudioSource sfxSource;
    [SerializeField] AudioClip ringClip;
    [SerializeField] AudioClip pickupClip;
    [SerializeField] AudioClip hangupClip;
    [SerializeField] AudioClip glitchClip;
    [Tooltip("Panjang satu siklus nada sambung (detik). Sama dengan panjang file ringback.")]
    [SerializeField] float ringPeriod = 3f;
    [Tooltip("Bagian awal tiap siklus yang bunyi (HP bergetar di bagian ini).")]
    [SerializeField] float ringToneLength = 1f;
    [Tooltip("Minimal berapa detik berdering sebelum bisa diangkat.")]
    [SerializeField] float minRingSeconds = 3.2f;
    [SerializeField] float pickupPause = 0.45f;

    public CallState State { get; private set; } = CallState.Hidden;
    /// <summary>True selama nunggu telpon diangkat. DialogueManager menahan teks selama ini.</summary>
    public bool BlocksDialogue { get; private set; }
    public float Elapsed => elapsed;

    Layout layout;
    Vector2 tbOrigPos;
    Vector3 tbOrigScale;
    Sprite nameNormalSprite;
    float elapsed;
    float ringStart;
    float? pendingTime;
    bool rolling;
    bool glitchHard;
    Coroutine moveRoutine, connectRoutine, glitchRoutine, endRoutine, rollRoutine, popRoutine;

    // ---------- Setup ----------

    void Awake()
    {
        Instance = this;
        layout = findlayout(defaultLayout) ?? (layouts.Length > 0 ? layouts[0] : new Layout());

        if (textbox != null)
        {
            tbOrigPos = textbox.anchoredPosition;
            tbOrigScale = textbox.localScale;
        }

        if (nameImage != null)
            nameNormalSprite = nameImage.sprite;
        if (ringSource != null)
        {
            ringSource.loop = true;
            ringSource.playOnAwake = false;
        }

        if (sfxSource != null)
            sfxSource.playOnAwake = false;

        resetglitchvisuals();
        phoneGroup.alpha = 0f;
        phoneGroup.blocksRaycasts = false;
        phoneGroup.interactable = false;
        phoneRoot.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---------- Tag Ink ----------

    /// <summary>Baca satu tag Ink. Return true kalau tag-nya milik telpon.</summary>
    public bool handletag(string key, string value)
    {
        switch (key)
        {
            case "call":
                switch (value.Trim().ToLowerInvariant())
                {
                    case "calling": case "ring": case "start":    startcall(); break;
                    case "connected": case "connect": case "answer": case "pickup": connect(); break;
                    case "glitch":                                startglitch(false); break;
                    case "glitch_hard": case "glitch hard":       startglitch(true); break;
                    case "stable": case "unglitch":               stopglitch(); break;
                    case "end": case "hangup":                    endcall(false); break;
                    case "lost": case "failed": case "cut":       endcall(true); break;
                    case "hide":                                  hideimmediate(); break;
                    default:
                        Debug.LogWarning("[PhoneCallUI] #call: '" + value + "' tidak dikenal.");
                        break;
                }

                return true;
            case "call_time":
                settime(value);
                return true;
            case "call_layout":
                setlayout(value);
                return true;
        }

        return false;
    }

    // ---------- API utama ----------

    public void startcall()
    {
        stopallcallroutines();
        resetglitchvisuals();
        bool wasHidden = State == CallState.Hidden;
        State = CallState.Calling;
        ringStart = Time.time;
        elapsed = 0f;
        setstatuscolor(statusColor);

        if (ringSource != null && ringClip != null)
        {
            ringSource.clip = ringClip;
            ringSource.Play();
        }

        if (wasHidden)
            moveRoutine = StartCoroutine(show());
    }

    public void connect()
    {
        if (State == CallState.Hidden)
        {
            // dipanggil tanpa #call: calling dulu -> langsung tampil, langsung tersambung
            startcall();
            finishpickupnow();
            return;
        }

        if (State != CallState.Calling)
            return; // sudah tersambung
        if (connectRoutine != null)
            StopCoroutine(connectRoutine);
        connectRoutine = StartCoroutine(connectroutine());
    }

    public void startglitch(bool hard)
    {
        if (State == CallState.Hidden || State == CallState.Ending)
            return;
        if (State == CallState.Calling)
        {
            if (connectRoutine != null)
                StopCoroutine(connectRoutine);
            connectRoutine = null;
            BlocksDialogue = false;
            finishpickupnow();
        }

        glitchHard = hard;
        State = CallState.Glitch;
        if (glitchRoutine == null)
            glitchRoutine = StartCoroutine(glitchroutine());
    }

    public void stopglitch()
    {
        if (State != CallState.Glitch)
            return;
        State = CallState.Connected;
        if (glitchRoutine != null)
            StopCoroutine(glitchRoutine);
        glitchRoutine = null;
        resetglitchvisuals();
        setstatus(formattime(elapsed));
    }

    public void endcall(bool failed)
    {
        if (State == CallState.Hidden || State == CallState.Ending)
            return;
        stopallcallroutines();
        endRoutine = StartCoroutine(endroutine(failed));
    }

    public void hideimmediate()
    {
        stopallcallroutines();
        if (ringSource != null)
            ringSource.Stop();
        resetglitchvisuals();
        State = CallState.Hidden;
        BlocksDialogue = false;
        phoneGroup.alpha = 0f;
        phoneRoot.gameObject.SetActive(false);
        if (textbox != null)
        {
            textbox.anchoredPosition = tbOrigPos;
            textbox.localScale = tbOrigScale;
        }
    }

    /// <summary>"01:30", "1:02:05", atau "90" (detik).</summary>
    public void settime(string value)
    {
        if (!tryparsetime(value, out float target))
        {
            Debug.LogWarning("[PhoneCallUI] Format #call_time salah. Contoh: #call_time: 01:30");
            return;
        }

        if (State == CallState.Connected || State == CallState.Glitch)
        {
            if (rollRoutine != null)
                StopCoroutine(rollRoutine);
            rollRoutine = StartCoroutine(rollto(target));
        }
        else
        {
            pendingTime = target; // dipakai waktu diangkat
        }
    }

    public void setlayout(string id)
    {
        Layout l = findlayout(id.Trim());
        if (l == null)
        {
            Debug.LogWarning("[PhoneCallUI] Layout '" + id + "' tidak ada. Pilihan: right / center");
            return;
        }

        layout = l;
        if (State != CallState.Hidden && State != CallState.Ending)
        {
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(movetolayout(0.5f));
        }
    }

    // ---------- Update: dering, titik "calling...", timer ----------

    void Update()
    {
        switch (State)
        {
            case CallState.Calling:
                updatecallingtext();
                updatevibrate();
                break;
            case CallState.Connected:
                elapsed += Time.deltaTime * timerSpeed;
                if (!rolling && !BlocksDialogue)
                    setstatus(formattime(elapsed));
                break;
            case CallState.Glitch:
                elapsed += Time.deltaTime * timerSpeed;
                if (scanlines != null)
                {
                    Rect r = scanlines.uvRect;
                    r.y = (r.y + Time.deltaTime * (glitchHard ? 0.9f : 0.35f)) % 1f;
                    scanlines.uvRect = r;
                }

                break;
        }
    }

    void updatecallingtext()
    {
        // titik muncul satu-satu. Titik yang belum muncul dibuat transparan supaya teks tidak geser-geser
        int n = Mathf.FloorToInt(Time.time * 2.2f) % 4;
        string dots = new string('.', n) + "<alpha=#00>" + new string('.', 3 - n);
        setstatus(callingText + dots);

        Color c = statusColor;
        c.a *= 0.75f + 0.25f * Mathf.Sin(Time.time * 3.2f);
        setstatuscolor(c);
    }

    void updatevibrate()
    {
        float phase = (Time.time - ringStart) % ringPeriod;
        if (phase < ringToneLength)
        {
            float t = Time.time;
            shaker.anchoredPosition = new Vector2(Mathf.Sin(t * 95f) * 3.5f, Mathf.Sin(t * 71f) * 1.2f);
            shaker.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(t * 83f) * 0.8f);
        }
        else
        {
            shaker.anchoredPosition = Vector2.Lerp(shaker.anchoredPosition, Vector2.zero, Time.deltaTime * 20f);
            shaker.localRotation = Quaternion.Slerp(shaker.localRotation, Quaternion.identity, Time.deltaTime * 20f);
        }
    }

    // ---------- Coroutine ----------

    IEnumerator show()
    {
        phoneRoot.gameObject.SetActive(true);
        Vector2 end = layout.phonePos;
        Vector2 start = end + Vector2.down * slideDistance;
        Vector3 scale = Vector3.one * layout.phoneScale;
        phoneRoot.localScale = scale;

        Vector2 tbStart = textbox != null ? textbox.anchoredPosition : Vector2.zero;
        Vector3 tbStartScale = textbox != null ? textbox.localScale : Vector3.one;

        float t = 0f;
        while (t < showDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / showDuration);
            phoneRoot.anchoredPosition = Vector2.LerpUnclamped(start, end, easeoutback(k));
            phoneGroup.alpha = Mathf.Clamp01(k / 0.35f);
            if (textbox != null)
            {
                float e = easeinout(k);
                textbox.anchoredPosition = Vector2.Lerp(tbStart, layout.textboxPos, e);
                textbox.localScale = Vector3.Lerp(tbStartScale, Vector3.one * layout.textboxScale, e);
            }

            yield return null;
        }

        phoneRoot.anchoredPosition = end;
        phoneGroup.alpha = 1f;
        moveRoutine = null;
    }

    IEnumerator hide()
    {
        Vector2 start = phoneRoot.anchoredPosition;
        Vector2 end = start + Vector2.down * slideDistance;
        Vector2 tbStart = textbox != null ? textbox.anchoredPosition : Vector2.zero;
        Vector3 tbStartScale = textbox != null ? textbox.localScale : Vector3.one;

        float t = 0f;
        while (t < hideDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / hideDuration);
            phoneRoot.anchoredPosition = Vector2.LerpUnclamped(start, end, easeinback(k));
            phoneGroup.alpha = 1f - Mathf.Clamp01((k - 0.5f) / 0.5f);
            if (textbox != null)
            {
                float e = easeinout(k);
                textbox.anchoredPosition = Vector2.Lerp(tbStart, tbOrigPos, e);
                textbox.localScale = Vector3.Lerp(tbStartScale, tbOrigScale, e);
            }

            yield return null;
        }

        phoneGroup.alpha = 0f;
        phoneRoot.gameObject.SetActive(false);
        moveRoutine = null;
    }

    IEnumerator movetolayout(float duration)
    {
        Vector2 pStart = phoneRoot.anchoredPosition;
        Vector3 pStartScale = phoneRoot.localScale;
        Vector2 tbStart = textbox != null ? textbox.anchoredPosition : Vector2.zero;
        Vector3 tbStartScale = textbox != null ? textbox.localScale : Vector3.one;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float e = easeinout(Mathf.Clamp01(t / duration));
            phoneRoot.anchoredPosition = Vector2.Lerp(pStart, layout.phonePos, e);
            phoneRoot.localScale = Vector3.Lerp(pStartScale, Vector3.one * layout.phoneScale, e);
            if (textbox != null)
            {
                textbox.anchoredPosition = Vector2.Lerp(tbStart, layout.textboxPos, e);
                textbox.localScale = Vector3.Lerp(tbStartScale, Vector3.one * layout.textboxScale, e);
            }

            yield return null;
        }

        moveRoutine = null;
    }

    IEnumerator connectroutine()
    {
        BlocksDialogue = true;

        // biarkan berdering minimal sekian detik, lalu angkat di jeda antar dering (biar natural)
        while (Time.time - ringStart < minRingSeconds ||
               (Time.time - ringStart) % ringPeriod < ringToneLength + 0.05f)
            yield return null;

        finishpickupnow();
        yield return new WaitForSeconds(pickupPause);
        BlocksDialogue = false;
        connectRoutine = null;
    }

    void finishpickupnow()
    {
        if (ringSource != null)
            ringSource.Stop();
        playsfx(pickupClip, 1f);

        State = CallState.Connected;
        elapsed = pendingTime ?? 0f;
        pendingTime = null;
        shaker.anchoredPosition = Vector2.zero;
        shaker.localRotation = Quaternion.identity;

        setstatuscolor(statusColor);
        setstatus(formattime(elapsed));
        if (popRoutine != null)
            StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(popstatus());
    }

    IEnumerator popstatus()
    {
        RectTransform rt = statusText.rectTransform;
        float t = 0f, d = 0.3f;
        while (t < d)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / d);
            rt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, easeoutback(k));
            Color c = statusColor;
            c.a *= Mathf.Clamp01(k * 2f);
            setstatuscolor(c);
            yield return null;
        }

        rt.localScale = Vector3.one;
        setstatuscolor(statusColor);
        popRoutine = null;
    }

    IEnumerator rollto(float target)
    {
        rolling = true;
        float from = elapsed;
        float t = 0f;
        while (t < rollDuration)
        {
            t += Time.deltaTime;
            float v = Mathf.Lerp(from, target, easeinout(Mathf.Clamp01(t / rollDuration)));
            if (State == CallState.Connected)
                setstatus(formattime(v));
            yield return null;
        }

        elapsed = target;
        rolling = false;
        rollRoutine = null;
    }

    IEnumerator glitchroutine()
    {
        if (scanlines != null)
            scanlines.enabled = true;
        const string noise = "#%&@$!?/\\|<>*01";

        while (State == CallState.Glitch)
        {
            bool hard = glitchHard;

            // nama: kadang versi glitch
            if (nameImage != null && nameGlitchSprite != null)
                nameImage.sprite = Random.value < (hard ? 0.7f : 0.45f) ? nameGlitchSprite : nameNormalSprite;

            // bar merah-biru
            if (glitchBars != null)
            {
                bool on = Random.value < (hard ? 0.75f : 0.4f);
                setalpha(glitchBars, on ? Random.Range(0.55f, 1f) : 0f);
                glitchBars.rectTransform.anchoredPosition = glitchBarsBase + new Vector2(Random.Range(-10f, 10f), Random.Range(-25f, 25f));
            }

            // getar
            float amp = hard ? 14f : 5f;
            shaker.anchoredPosition = Random.value < 0.6f ? Random.insideUnitCircle * amp : Vector2.zero;
            shaker.localEulerAngles = new Vector3(0f, 0f, Random.Range(-1f, 1f) * (hard ? 2.2f : 0.6f));

            // teks status: Poor connection / timer yang rusak
            string s;
            float r = Random.value;
            if (r < 0.5f)
                s = poorConnectionText;
            else if (r < 0.8f)
                s = formattime(elapsed);
            else
                s = scramble(Random.value < 0.5f ? poorConnectionText : formattime(elapsed), noise, hard ? 0.45f : 0.25f);
            setstatus(s);
            setghostoffset(Random.Range(2f, hard ? 7f : 4f));

            // bunyi glitch
            if (Random.value < (hard ? 0.3f : 0.12f))
                playsfx(glitchClip, hard ? 0.8f : 0.5f, Random.Range(0.8f, 1.3f));

            // kedipan HP miring
            if (tiltedStack != null && Random.value < (hard ? 0.2f : 0.06f))
            {
                normalStack.SetActive(false);
                tiltedStack.SetActive(true);
                yield return new WaitForSeconds(Random.Range(0.05f, hard ? 0.18f : 0.1f));
                tiltedStack.SetActive(false);
                normalStack.SetActive(true);
            }

            // sesekali tenang sebentar supaya tidak capek dilihat
            if (!hard && Random.value < 0.25f)
            {
                calmframe();
                yield return new WaitForSeconds(Random.Range(0.3f, 0.8f));
            }
            else
            {
                yield return new WaitForSeconds(Random.Range(hard ? 0.03f : 0.05f, hard ? 0.1f : 0.18f));
            }
        }

        glitchRoutine = null;
    }

    void calmframe()
    {
        if (nameImage != null)
            nameImage.sprite = nameNormalSprite;
        if (glitchBars != null)
            setalpha(glitchBars, 0f);
        shaker.anchoredPosition = Vector2.zero;
        shaker.localRotation = Quaternion.identity;
        setstatus(poorConnectionText);
        setghostoffset(1.5f);
    }

    IEnumerator endroutine(bool failed)
    {
        State = CallState.Ending;
        BlocksDialogue = false;
        if (ringSource != null)
            ringSource.Stop();

        if (failed)
        {
            // putus: kedip glitch sebentar lalu "Call Failed" merah
            playsfx(glitchClip, 1f, 0.7f);
            for (int i = 0; i < 6; i++)
            {
                if (tiltedStack != null)
                {
                    normalStack.SetActive(i % 2 == 1);
                    tiltedStack.SetActive(i % 2 == 0);
                }

                shaker.anchoredPosition = Random.insideUnitCircle * 16f;
                yield return new WaitForSeconds(0.05f);
            }

            resetglitchvisuals();
            playsfx(hangupClip, 0.9f);
            setstatuscolor(failedColor);
            setstatus(failedText);
            for (int i = 0; i < 4; i++)
            {
                statusText.enabled = i % 2 == 0;
                yield return new WaitForSeconds(0.25f);
            }

            statusText.enabled = true;
            yield return new WaitForSeconds(0.6f);
        }
        else
        {
            resetglitchvisuals();
            playsfx(hangupClip, 1f);
            setstatus(endedText);
            for (int i = 0; i < 6; i++)
            {
                Color c = statusColor;
                c.a *= i % 2 == 0 ? 1f : 0.25f;
                setstatuscolor(c);
                yield return new WaitForSeconds(0.2f);
            }

            setstatuscolor(statusColor);
            yield return new WaitForSeconds(0.3f);
        }

        yield return hide();
        State = CallState.Hidden;
        setstatuscolor(statusColor);
        endRoutine = null;
    }

    // ---------- Helper ----------

    Vector2 glitchBarsBase;
    bool glitchBarsBaseSet;

    void resetglitchvisuals()
    {
        if (glitchBars != null)
        {
            if (!glitchBarsBaseSet)
            {
                glitchBarsBase = glitchBars.rectTransform.anchoredPosition;
                glitchBarsBaseSet = true;
            }

            glitchBars.rectTransform.anchoredPosition = glitchBarsBase;
            setalpha(glitchBars, 0f);
        }

        if (scanlines != null)
            scanlines.enabled = false;
        if (tiltedStack != null)
            tiltedStack.SetActive(false);
        if (normalStack != null)
            normalStack.SetActive(true);
        if (nameImage != null && nameNormalSprite != null)
            nameImage.sprite = nameNormalSprite;
        if (shaker != null)
        {
            shaker.anchoredPosition = Vector2.zero;
            shaker.localRotation = Quaternion.identity;
        }

        if (statusText != null)
            statusText.enabled = true;
        setghostoffset(0f);
    }

    void stopallcallroutines()
    {
        foreach (var c in new[] { moveRoutine, connectRoutine, glitchRoutine, endRoutine, rollRoutine, popRoutine })
            if (c != null)
                StopCoroutine(c);
        moveRoutine = connectRoutine = glitchRoutine = endRoutine = rollRoutine = popRoutine = null;
        rolling = false;
        BlocksDialogue = false;
        if (statusText != null)
            statusText.rectTransform.localScale = Vector3.one;
    }

    void setstatus(string s)
    {
        statusText.text = s;
        if (statusGhostCyan != null)
            statusGhostCyan.text = s;
        if (statusGhostRed != null)
            statusGhostRed.text = s;
    }

    void setstatuscolor(Color c)
    {
        statusText.color = c;
    }

    // efek RGB split: dua salinan teks cyan & merah digeser kiri-kanan
    void setghostoffset(float px)
    {
        if (statusGhostCyan != null)
        {
            statusGhostCyan.enabled = px > 0.01f;
            statusGhostCyan.rectTransform.anchoredPosition = statusText.rectTransform.anchoredPosition + new Vector2(-px, 0f);
        }

        if (statusGhostRed != null)
        {
            statusGhostRed.enabled = px > 0.01f;
            statusGhostRed.rectTransform.anchoredPosition = statusText.rectTransform.anchoredPosition + new Vector2(px, 0f);
        }
    }

    void playsfx(AudioClip clip, float volume, float pitch = 1f)
    {
        if (sfxSource == null || clip == null)
            return;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume);
    }

    Layout findlayout(string id)
    {
        foreach (var l in layouts)
            if (l != null && string.Equals(l.id, id, System.StringComparison.OrdinalIgnoreCase))
                return l;
        return null;
    }

    static string formattime(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int h = s / 3600, m = (s / 60) % 60, sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m:00}:{sec:00}";
    }

    static bool tryparsetime(string value, out float seconds)
    {
        seconds = 0f;
        string[] p = value.Trim().Split(':');
        float total = 0f;
        foreach (string part in p)
        {
            if (!float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return false;
            total = total * 60f + v;
        }

        seconds = total;
        return p.Length > 0;
    }

    static string scramble(string s, string noise, float amount)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (c[i] != ' ' && Random.value < amount)
                c[i] = noise[Random.Range(0, noise.Length)];
        return new string(c);
    }

    static void setalpha(Graphic g, float a)
    {
        Color c = g.color;
        c.a = a;
        g.color = c;
    }

    static float easeinout(float k) => k * k * (3f - 2f * k);

    static float easeoutback(float k)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    static float easeinback(float k)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        return c3 * k * k * k - c1 * k * k;
    }
}
