using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// Networked 4-player euchre with bots.
/// Owner-authoritative: every player sends actions to the instance owner with NetAction; only the owner changes
/// the synced state, then serializes it and every client rebuilds the table visuals. Bots run on the owner.
/// Seats 0 and 2 are team A, seats 1 and 3 are team B. Card ids: see EuchreCard.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class EuchreGame : UdonSharpBehaviour
{
    [Header("Wired by Tools > Euchre > Build Table")]
    public TMP_Text statusText;
    public Transform statusAnchor;
    public TMP_Text[] seatLabels;
    public EuchreCard[] handCards;   // 4 seats x 6 slots (slot 5 = dealer pickup)
    public EuchreCard[] trickCards;  // one per seat
    public EuchreCard upcard;
    public TMP_Text trumpText;       // big trump suit marker in the middle of the table
    public EuchreButton[] actionButtons; // 4 seats x ButtonSlots context buttons
    public Transform[] trickSpots;       // each seat's played card, turned toward the viewer locally
    public Transform upcardSpot;
    public Transform trumpSpot;
    public Renderer[] tableRenderers;
    public Renderer[] buttonRenderers;
    public Renderer[] sitButtonRenderers;

    [Header("Materials (drop a material in to restyle everything that uses it)")]
    public Material tableMaterial;
    public Material cardFaceMaterial;
    [Tooltip("Optional. If empty, card backs are the face material tinted blue.")]
    public Material cardBackMaterial;
    public Material buttonMaterial;
    public Material sitButtonMaterial;
    public AudioSource sfxSource;

    [Header("Rules")]
    public int pointsToWin = 10;
    [Tooltip("If everyone passes twice, the dealer must name trump instead of redealing.")]
    public bool stickTheDealer = false;

    [Header("Timing")]
    [Tooltip("How long a bot 'thinks' before each move.")]
    public float botThinkSeconds = 0.9f;
    [Tooltip("Extra time a finished trick stays on the table before a bot leads the next one.")]
    public float trickPauseSeconds = 1.5f;
    [Tooltip("Seconds before the next hand is dealt on its own. 0 = wait for Next Hand.")]
    public float nextHandSeconds = 6f;

    [Header("Readable table")]
    [Tooltip("On your screen, other players' played cards, the upcard and the trump marker turn to face you and tilt up.")]
    public bool faceCardsToViewer = true;
    public float cardTilt = 55f;
    [Tooltip("Size of the turned cards on your screen.")]
    public float cardScale = 1.5f;

    [Header("Sounds (all optional: leave a slot empty for silence)")]
    [Tooltip("A new hand is dealt.")]
    public AudioClip dealSound;
    [Tooltip("A card lands on the table (plays and the dealer's discard). With several clips, one is picked at random.")]
    public AudioClip[] cardSounds;
    public AudioClip passSound;
    [Tooltip("Someone orders up, picks up, or names trump.")]
    public AudioClip trumpCalledSound;
    [Tooltip("Optional: played instead of the trump sound when the maker goes alone.")]
    public AudioClip goingAloneSound;
    public AudioClip trickWonSound;
    [Tooltip("The makers took 3 or 4 tricks.")]
    public AudioClip madeItSound;
    [Tooltip("All 5 tricks (falls back to the made-it sound when empty).")]
    public AudioClip marchSound;
    public AudioClip euchredSound;
    public AudioClip gameWonSound;
    [Tooltip("Played for you only when it becomes your turn to bid, discard or play.")]
    public AudioClip yourTurnSound;
    [Range(0f, 1f)] public float soundVolume = 0.8f;
    [Tooltip("Random pitch change on each card, so repeated sounds don't sound identical.")]
    public float cardPitchVariation = 0.06f;

    [Header("Testing")]
    [Tooltip("Lets the local player act for every seat and start without 4 players.")]
    public bool debugControlAllSeats = false;

    const int PhaseLobby = 0;
    const int PhaseBid1 = 1;        // order up / pass on the upcard
    const int PhaseBid2 = 2;        // name a different suit / pass
    const int PhaseDiscard = 3;     // dealer discards after picking up
    const int PhasePlay = 4;
    const int PhaseHandOver = 5;
    const int PhaseGameOver = 6;

    const int ActSit = 0;
    const int ActBotToggle = 1;
    const int ActStartGame = 2;
    const int ActNext = 3;
    const int ActResetGame = 4;
    const int ActPassBid = 5;
    const int ActOrder = 6;         // arg: 1 = alone
    const int ActCallSuit = 7;      // arg: suit + 4 if alone
    const int ActPlayCard = 8;      // arg: card id
    const int ActDiscardCard = 9;   // arg: card id

    // What the latest synced sound event is.
    const int SfxNone = 0;
    const int SfxDeal = 1;
    const int SfxPass = 2;
    const int SfxTrump = 3;
    const int SfxAlone = 4;
    const int SfxDiscard = 5;
    const int SfxTrick = 6;
    const int SfxMadeIt = 7;
    const int SfxMarch = 8;
    const int SfxEuchred = 9;
    const int SfxGameWon = 10;

    const int BotSeat = -2;         // seatPlayers value for a bot-controlled seat

    // Context buttons: each seat has ButtonSlots buttons that show only the choices available right now.
    public const int ButtonSlots = 5;
    const int BtnNone = 0;
    const int BtnOrder = 1;
    const int BtnOrderAlone = 2;
    const int BtnPass = 3;
    const int BtnSuit = 4;          // arg: suit
    const int BtnAloneToggle = 5;
    const int BtnStart = 6;
    const int BtnNext = 7;
    const int BtnReset = 8;
    const int HandSize = 6;

    [UdonSynced] int[] seatPlayers = { -1, -1, -1, -1 };
    [UdonSynced] int phase = PhaseLobby;
    [UdonSynced] int dealer = 0;
    [UdonSynced] int turn = 0;
    [UdonSynced] int[] hands = new int[24];
    [UdonSynced] int upcardId = -1;
    [UdonSynced] int turnedDown = -1;   // suit of the upcard once it's turned down
    [UdonSynced] int trump = -1;
    [UdonSynced] int maker = -1;        // seat that named trump
    [UdonSynced] bool goingAlone = false;
    [UdonSynced] int sittingOut = -1;   // partner of a lone maker
    [UdonSynced] int[] trick = { -1, -1, -1, -1 };
    [UdonSynced] int leadSeat = -1;
    [UdonSynced] int cardsInTrick = 0;
    [UdonSynced] int[] tricksWon = new int[2];
    [UdonSynced] int[] score = new int[2];
    [UdonSynced] int playedMask = 0;    // bit per card id played this hand
    [UdonSynced] int timerDeadline = 0; // server time (ms) the next hand deals itself
    [UdonSynced] string lastEvent = "";
    [UdonSynced] int sfxKind = SfxNone;
    [UdonSynced] int sfxSerial = 0;

    // ---- local only ----
    bool localAlone = false;    // the local player's "Alone" toggle, sent along with their bid
    bool botStepQueued = false; // owner only
    bool timerQueued = false;
    float nextCountdownRefresh = 0f;
    Vector3[] trickHome = new Vector3[4];
    Quaternion[] trickHomeRot = new Quaternion[4];
    Vector3 upHome, trumpHome;
    Quaternion upHomeRot, trumpHomeRot;
    bool homesSaved = false;
    int[] slotCode = new int[20];
    int[] slotArg = new int[20];
    int slotFill = 0;
    bool soundsPrimed = false;  // late joiners don't replay the last event
    bool gotState = false;
    int heardSfx = 0;
    int heardCards = 0;
    int heardTurnKey = -1;

    string[] suitNames = { "Clubs", "Spades", "Hearts", "Diamonds" };
    string[] suitSymbols = { "♣", "♠", "♥", "♦" };
    string[] seatNames = { "South", "West", "North", "East" };

    void Start()
    {
        for (int i = 0; i < hands.Length; i++) hands[i] = -1;
        ApplyMaterials();
        Refresh();
    }

    /// Pushes the material slots onto every card, button and table part.
    public void ApplyMaterials()
    {
        ApplyCardMaterials(handCards);
        ApplyCardMaterials(trickCards);
        if (upcard != null) upcard.SetMaterials(cardFaceMaterial, cardBackMaterial);
        ApplyRendererMaterial(tableRenderers, tableMaterial);
        ApplyRendererMaterial(buttonRenderers, buttonMaterial);
        ApplyRendererMaterial(sitButtonRenderers, sitButtonMaterial);
    }

    void ApplyCardMaterials(EuchreCard[] cards)
    {
        if (cards == null) return;
        for (int i = 0; i < cards.Length; i++) if (cards[i] != null) cards[i].SetMaterials(cardFaceMaterial, cardBackMaterial);
    }

    void ApplyRendererMaterial(Renderer[] renderers, Material m)
    {
        if (renderers == null || m == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].sharedMaterial = m;
    }

    void Update()
    {
        UpdateFacing();
        if (phase == PhaseHandOver && nextHandSeconds > 0f && statusText != null && Time.time >= nextCountdownRefresh)
        {
            nextCountdownRefresh = Time.time + 0.25f;
            statusText.text = BuildStatus();
        }
        if (statusAnchor == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;
        Vector3 dir = statusAnchor.position - local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) statusAnchor.rotation = Quaternion.LookRotation(dir);
    }

    public override void OnDeserialization()
    {
        gotState = true;
        Refresh();
    }

    /// Owner: seats whose player is no longer in the instance go to a bot (mid-game) or open up. Covers a player
    /// who left while ownership was moving, when no owner was around to handle OnPlayerLeft.
    void ReleaseMissingPlayers()
    {
        bool changed = false;
        for (int s = 0; s < 4; s++)
        {
            if (seatPlayers[s] < 0) continue;
            if (Utilities.IsValid(VRCPlayerApi.GetPlayerById(seatPlayers[s]))) continue;
            seatPlayers[s] = IsRunning() ? BotSeat : -1;
            changed = true;
        }
        if (!changed) return;
        lastEvent = IsRunning() ? "A player left. A bot took over their seat." : "A player left.";
        Commit();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        Refresh(); // seat names come from the player list
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject) || player == null) return;
        bool changed = false;
        for (int s = 0; s < 4; s++)
        {
            if (seatPlayers[s] != player.playerId) continue;
            seatPlayers[s] = IsRunning() ? BotSeat : -1;
            changed = true;
        }
        if (!changed) return;
        if (IsRunning()) lastEvent = "A player left. A bot took over their seat.";
        Commit();
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // A new owner picks up the bots and the next-hand timer.
        if (!Networking.IsOwner(gameObject)) return;
        botStepQueued = false;
        timerQueued = false;
        ReleaseMissingPlayers();
        ScheduleBots();
        ScheduleNextHand();
    }

    // ================= local input =================

    public void ToggleSeat(int s) { Send(s, ActSit, 0); }
    public void ToggleBot(int s) { Send(s, ActBotToggle, 0); }

    public void ActStart() { SendAsSeated(ActStartGame); }
    public void ActNextHand() { SendAsSeated(ActNext); }
    public void ActReset() { SendAsSeated(ActResetGame); }

    void SendAsSeated(int action)
    {
        int s = LocalSeat();
        if (s < 0 && !debugControlAllSeats) { Hint("Sit down first."); return; }
        Send(s < 0 ? 0 : s, action, 0);
    }

    public void ActToggleAlone()
    {
        localAlone = !localAlone;
        Refresh();
    }

    public void ActPass()
    {
        int s = BiddingSeat();
        if (s >= 0) Send(s, ActPassBid, 0);
    }

    public void ActOrderUp()
    {
        int s = BiddingSeat();
        if (s < 0) return;
        if (phase != PhaseBid1) { Hint("Ordering up is only allowed on the first round."); return; }
        Send(s, ActOrder, localAlone ? 1 : 0);
        localAlone = false;
    }

    public void ActCallClubs() { CallSuit(0); }
    public void ActCallSpades() { CallSuit(1); }
    public void ActCallHearts() { CallSuit(2); }
    public void ActCallDiamonds() { CallSuit(3); }

    void CallSuit(int suit)
    {
        int s = BiddingSeat();
        if (s < 0) return;
        if (phase != PhaseBid2) { Hint("You can only name a suit on the second round."); return; }
        if (suit == turnedDown) { Hint(suitNames[suit] + " was turned down. Pick another suit."); return; }
        Send(s, ActCallSuit, suit + (localAlone ? 4 : 0));
        localAlone = false;
    }

    public void OnCardClicked(int s, int slot)
    {
        if (!ControlsSeat(s)) { Hint("Those aren't your cards."); return; }
        int card = hands[s * HandSize + slot];
        if (card < 0) return;
        if (phase == PhaseDiscard)
        {
            if (s != dealer) { Hint("Waiting for the dealer to discard."); return; }
            Send(s, ActDiscardCard, card);
            return;
        }
        if (phase != PhasePlay) { Hint("It's not time to play a card."); return; }
        if (s != turn) { Hint("It's " + SeatName(turn) + "'s turn."); return; }
        if (!CanPlay(s, card)) { Hint("You must follow " + suitNames[EffectiveSuit(trick[leadSeat])] + "."); return; }
        Send(s, ActPlayCard, card);
    }

    void Send(int seat, int action, int arg)
    {
        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetAction), seat, action, arg);
    }

    // ================= owner: action handling =================

    [NetworkCallable]
    public void NetAction(int seat, int action, int arg)
    {
        if (!Networking.IsOwner(gameObject) || seat < 0 || seat > 3) return;
        VRCPlayerApi caller = NetworkCalling.CallingPlayer;
        if (!Utilities.IsValid(caller)) return;
        int pid = caller.playerId;

        if (action == ActSit) { DoSit(seat, caller); return; }
        if (action == ActBotToggle) { DoToggleBot(seat); return; }

        bool seated = debugControlAllSeats || FindSeat(pid) >= 0;
        if (!seated) return;
        if (action == ActStartGame) { DoStartGame(); return; }
        if (action == ActNext) { DoNextHand(); return; }
        if (action == ActResetGame) { DoReset(); return; }

        if (IsBot(seat) || (!debugControlAllSeats && seatPlayers[seat] != pid)) return;
        if (action == ActPassBid) DoPass(seat);
        else if (action == ActOrder) DoOrderUp(seat, arg == 1);
        else if (action == ActCallSuit) DoCallSuit(seat, arg % 4, arg >= 4);
        else if (action == ActPlayCard) DoPlay(seat, arg);
        else if (action == ActDiscardCard) DoDiscard(seat, arg);
    }

    void DoSit(int s, VRCPlayerApi caller)
    {
        int pid = caller.playerId;
        bool running = IsRunning();
        if (seatPlayers[s] == pid)
        {
            // Standing up mid-game hands the seat to a bot so the game can continue.
            seatPlayers[s] = running ? BotSeat : -1;
            lastEvent = caller.displayName + " stood up." + (running ? " A bot took over." : "");
            Commit();
            return;
        }
        bool takingOverBot = seatPlayers[s] == BotSeat;
        if (seatPlayers[s] != -1 && !takingOverBot) return;
        if (running && !takingOverBot && !debugControlAllSeats) return;
        for (int i = 0; i < 4; i++) if (seatPlayers[i] == pid) seatPlayers[i] = running ? BotSeat : -1;
        seatPlayers[s] = pid;
        lastEvent = caller.displayName + (takingOverBot ? " took over from the bot." : " sat at " + seatNames[s] + ".");
        Commit();
    }

    void DoToggleBot(int s)
    {
        if (IsRunning()) return; // mid-game, sit on a bot's seat to take it over instead
        if (seatPlayers[s] == -1) { seatPlayers[s] = BotSeat; lastEvent = SeatName(s) + " joined."; }
        else if (seatPlayers[s] == BotSeat) { seatPlayers[s] = -1; lastEvent = "Bot removed."; }
        else return;
        Commit();
    }

    void DoStartGame()
    {
        if (IsRunning()) return;
        if (!debugControlAllSeats && SeatedCount() < 4) { lastEvent = "Need 4 players (or bots) to start."; Commit(); return; }
        score[0] = 0;
        score[1] = 0;
        dealer = Random.Range(0, 4);
        Deal();
        lastEvent = "New game! " + SeatName(dealer) + " deals.";
        Commit();
    }

    void DoNextHand()
    {
        if (phase != PhaseHandOver) return;
        dealer = (dealer + 1) % 4;
        Deal();
        lastEvent = SeatName(dealer) + " deals.";
        Commit();
    }

    void DoReset()
    {
        phase = PhaseLobby;
        ClearHand();
        score[0] = 0;
        score[1] = 0;
        lastEvent = "Game reset.";
        Commit();
    }

    void DoPass(int s)
    {
        if ((phase != PhaseBid1 && phase != PhaseBid2) || s != turn) return;
        if (phase == PhaseBid1)
        {
            lastEvent = SeatName(s) + " passed.";
            Sfx(SfxPass);
            if (s == dealer)
            {
                phase = PhaseBid2;
                turnedDown = upcardId / 6;
                upcardId = -1;
                turn = NextSeat(dealer);
                lastEvent += " " + suitNames[turnedDown] + " turned down.";
            }
            else turn = NextSeat(s);
        }
        else
        {
            if (s == dealer)
            {
                if (stickTheDealer) { lastEvent = "Stick the dealer: " + SeatName(s) + " must name trump."; Commit(); return; }
                dealer = (dealer + 1) % 4;
                Deal();
                lastEvent = "Everyone passed. " + SeatName(dealer) + " redeals.";
            }
            else
            {
                lastEvent = SeatName(s) + " passed.";
                Sfx(SfxPass);
                turn = NextSeat(s);
            }
        }
        Commit();
    }

    void DoOrderUp(int s, bool alone)
    {
        if (phase != PhaseBid1 || s != turn) return;
        SetTrump(s, upcardId / 6, alone);
        lastEvent = SeatName(s) + (s == dealer ? " picked it up" : " ordered it up") + ": " + suitNames[trump] + (goingAlone ? ", going alone!" : ".");
        if (goingAlone && sittingOut == dealer)
        {
            // Dealer is the lone maker's partner and sits out, so the upcard stays down.
            upcardId = -1;
            BeginPlay();
        }
        else
        {
            hands[dealer * HandSize + 5] = upcardId;
            upcardId = -1;
            phase = PhaseDiscard;
            turn = dealer;
            lastEvent += " " + SeatName(dealer) + " must discard.";
        }
        Commit();
    }

    void DoCallSuit(int s, int suit, bool alone)
    {
        if (phase != PhaseBid2 || s != turn || suit == turnedDown) return;
        SetTrump(s, suit, alone);
        lastEvent = SeatName(s) + " called " + suitNames[suit] + (goingAlone ? ", going alone!" : ".");
        BeginPlay();
        Commit();
    }

    void DoDiscard(int s, int card)
    {
        if (phase != PhaseDiscard || s != dealer) return;
        int slot = SlotOf(s, card);
        if (slot < 0) return;
        hands[s * HandSize + slot] = -1;
        lastEvent = SeatName(dealer) + " discarded.";
        Sfx(SfxDiscard);
        BeginPlay();
        Commit();
    }

    void DoPlay(int s, int card)
    {
        if (phase != PhasePlay || s != turn) return;
        int slot = SlotOf(s, card);
        if (slot < 0 || !CanPlay(s, card)) return;

        // A finished trick stays on the table until its winner leads the next card.
        if (TrickComplete()) ClearTrick();
        if (cardsInTrick == 0) leadSeat = s;
        trick[s] = card;
        hands[s * HandSize + slot] = -1;
        playedMask |= 1 << card;
        cardsInTrick++;

        if (cardsInTrick < PlayersInHand())
        {
            turn = NextSeat(s);
            lastEvent = SeatName(s) + " played " + CardName(card) + ".";
        }
        else FinishTrick();
        Commit();
    }

    // ================= rules =================

    void Deal()
    {
        int[] deck = new int[24];
        for (int i = 0; i < 24; i++) deck[i] = i;
        for (int i = 23; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = deck[i]; deck[i] = deck[j]; deck[j] = t;
        }

        ClearHand();
        for (int s = 0; s < 4; s++)
            for (int k = 0; k < 5; k++)
                hands[s * HandSize + k] = deck[s * 5 + k];
        upcardId = deck[20];
        phase = PhaseBid1;
        turn = NextSeat(dealer);
        Sfx(SfxDeal);
    }

    void ClearHand()
    {
        for (int i = 0; i < hands.Length; i++) hands[i] = -1;
        for (int i = 0; i < 4; i++) trick[i] = -1;
        upcardId = -1;
        turnedDown = -1;
        trump = -1;
        maker = -1;
        goingAlone = false;
        sittingOut = -1;
        leadSeat = -1;
        cardsInTrick = 0;
        tricksWon[0] = 0;
        tricksWon[1] = 0;
        playedMask = 0;
    }

    void SetTrump(int s, int suit, bool alone)
    {
        trump = suit;
        maker = s;
        goingAlone = alone;
        sittingOut = alone ? (s + 2) % 4 : -1;
        Sfx(alone ? SfxAlone : SfxTrump);
    }

    void BeginPlay()
    {
        phase = PhasePlay;
        ClearTrick();
        turn = NextSeat(dealer);
    }

    void ClearTrick()
    {
        for (int i = 0; i < 4; i++) trick[i] = -1;
        cardsInTrick = 0;
        leadSeat = -1;
    }

    void FinishTrick()
    {
        int winner = TrickWinner();
        tricksWon[winner % 2]++;
        turn = winner;
        lastEvent = SeatName(winner) + " took the trick.";
        Sfx(SfxTrick);
        if (tricksWon[0] + tricksWon[1] >= 5) ScoreHand();
    }

    int TrickWinner()
    {
        int led = EffectiveSuit(trick[leadSeat]);
        int winner = leadSeat;
        int best = -1;
        for (int i = 0; i < 4; i++)
        {
            if (trick[i] < 0) continue;
            int v = Strength(trick[i], led);
            if (v > best) { best = v; winner = i; }
        }
        return winner;
    }

    void ScoreHand()
    {
        int makers = maker % 2;
        int defenders = 1 - makers;
        int taken = tricksWon[makers];
        int points;
        int team;
        string result;
        if (taken == 5)
        {
            team = makers;
            points = goingAlone ? 4 : 2;
            result = goingAlone ? "won all 5 alone" : "took all 5 (march)";
        }
        else if (taken >= 3)
        {
            team = makers;
            points = 1;
            result = "made it";
        }
        else
        {
            team = defenders;
            points = 2;
            result = "got euchred";
        }
        score[team] += points;
        Sfx(team != makers ? SfxEuchred : taken == 5 ? SfxMarch : SfxMadeIt);
        lastEvent = TeamName(makers) + " " + result + ". " + TeamName(team) + " +" + points + ".";

        if (score[team] >= pointsToWin)
        {
            phase = PhaseGameOver;
            lastEvent += " " + TeamName(team) + " win the game!";
            Sfx(SfxGameWon);
        }
        else
        {
            phase = PhaseHandOver;
            if (nextHandSeconds > 0f)
            {
                timerDeadline = Networking.GetServerTimeInMilliseconds() + (int)(nextHandSeconds * 1000f);
                ScheduleNextHand();
            }
        }
    }

    void ScheduleNextHand()
    {
        if (timerQueued || phase != PhaseHandOver || nextHandSeconds <= 0f || !Networking.IsOwner(gameObject)) return;
        timerQueued = true;
        int ms = timerDeadline - Networking.GetServerTimeInMilliseconds();
        SendCustomEventDelayedSeconds(nameof(_OnNextHandTimer), ms > 0 ? ms / 1000f + 0.05f : 0.1f);
    }

    public void _OnNextHandTimer()
    {
        timerQueued = false;
        if (!Networking.IsOwner(gameObject) || phase != PhaseHandOver) return;
        if (timerDeadline - Networking.GetServerTimeInMilliseconds() > 100) { ScheduleNextHand(); return; }
        DoNextHand();
    }

    /// Suit a card counts as once trump is known (the left bower belongs to trump).
    int EffectiveSuit(int card)
    {
        int suit = card / 6;
        if (trump >= 0 && card % 6 == 2 && suit == (trump ^ 1)) return trump;
        return suit;
    }

    /// Trick-taking strength with suit led: bowers 100/99, trump 50+, following suit 10+, anything else 0.
    int Strength(int card, int led)
    {
        int suit = card / 6;
        int rank = card % 6;
        if (rank == 2 && suit == trump) return 100;          // right bower
        if (rank == 2 && suit == (trump ^ 1)) return 99;     // left bower
        int eff = EffectiveSuit(card);
        if (eff == trump) return 50 + rank;
        if (eff == led) return 10 + rank;
        return 0;
    }

    bool IsLegal(int s, int card)
    {
        if (cardsInTrick == 0) return true;
        int led = EffectiveSuit(trick[leadSeat]);
        if (EffectiveSuit(card) == led) return true;
        for (int k = 0; k < HandSize; k++)
        {
            int c = hands[s * HandSize + k];
            if (c >= 0 && EffectiveSuit(c) == led) return false;
        }
        return true;
    }

    bool TrickComplete()
    {
        return cardsInTrick >= PlayersInHand();
    }

    bool CanPlay(int s, int card)
    {
        return TrickComplete() || IsLegal(s, card);
    }

    int NextSeat(int s)
    {
        int n = (s + 1) % 4;
        if (n == sittingOut) n = (n + 1) % 4;
        return n;
    }

    int PlayersInHand()
    {
        return sittingOut >= 0 ? 3 : 4;
    }

    int SlotOf(int s, int card)
    {
        for (int k = 0; k < HandSize; k++) if (hands[s * HandSize + k] == card) return k;
        return -1;
    }

    // ================= bots (run by the owner) =================

    void ScheduleBots()
    {
        if (botStepQueued || !Networking.IsOwner(gameObject) || BotToAct() < 0) return;
        botStepQueued = true;
        float delay = botThinkSeconds > 0f ? botThinkSeconds : 0.05f;
        if (phase == PhasePlay && TrickComplete()) delay += trickPauseSeconds;
        SendCustomEventDelayedSeconds(nameof(_BotStep), delay);
    }

    public void _BotStep()
    {
        botStepQueued = false;
        if (!Networking.IsOwner(gameObject)) return;
        int s = BotToAct();
        if (s < 0) return;
        if (phase == PhaseBid1) BotBid1(s);
        else if (phase == PhaseBid2) BotBid2(s);
        else if (phase == PhaseDiscard) DoDiscard(s, BotDiscardChoice(s, trump));
        else if (phase == PhasePlay) DoPlay(s, BotChoosePlay(s));
    }

    int BotToAct()
    {
        if (phase == PhaseDiscard) return IsBot(dealer) ? dealer : -1;
        if (phase == PhaseBid1 || phase == PhaseBid2 || phase == PhasePlay) return IsBot(turn) ? turn : -1;
        return -1;
    }

    // Hand strength is in points: a solid order-up is about 55 (a bower, two more trump and an ace);
    // going alone needs about 90.
    const int CallThreshold = 55;
    const int AloneThreshold = 90;

    void BotBid1(int s)
    {
        int suit = upcardId / 6;
        int v;
        if (s == dealer) v = BotDealerValue(s, suit);
        else
        {
            v = BotHandValue(s, suit, -1);
            // The upcard goes to the dealer: good for us if that's our partner, bad if it's an opponent.
            int up = BotCardValue(upcardId, suit, suit);
            v += (dealer % 2 == s % 2) ? up / 2 : -up / 2;
        }
        if (v >= CallThreshold) DoOrderUp(s, v >= AloneThreshold && BotHoldsRight(s, suit));
        else DoPass(s);
    }

    void BotBid2(int s)
    {
        int bestSuit = -1;
        int best = -999;
        for (int suit = 0; suit < 4; suit++)
        {
            if (suit == turnedDown) continue;
            int v = BotHandValue(s, suit, -1);
            if (v > best) { best = v; bestSuit = suit; }
        }
        bool forced = stickTheDealer && s == dealer;
        if (best >= CallThreshold || forced) DoCallSuit(s, bestSuit, best >= AloneThreshold && BotHoldsRight(s, bestSuit));
        else DoPass(s);
    }

    /// Value of the dealer's hand if they pick up the upcard (and drop their worst card).
    int BotDealerValue(int s, int suit)
    {
        int drop = BotDiscardChoiceWithExtra(s, suit, upcardId);
        int v = BotHandValue(s, suit, upcardId) - BotCardValue(drop, suit, suit);
        return v;
    }

    /// Points for a hand (plus an optional extra card) with the given trump.
    int BotHandValue(int s, int suit, int extra)
    {
        int v = 0;
        int trumps = 0;
        int[] counts = new int[4];
        for (int k = 0; k <= HandSize; k++)
        {
            int c = k < HandSize ? hands[s * HandSize + k] : extra;
            if (c < 0) continue;
            v += BotCardValue(c, suit, suit);
            int eff = BotEffSuit(c, suit);
            counts[eff]++;
            if (eff == suit) trumps++;
        }
        if (trumps >= 2) for (int x = 0; x < 4; x++) if (x != suit && counts[x] == 0) v += 5; // voids let trump ruff
        return v;
    }

    /// Bid value of one card: bowers and high trump are worth the most, off-suit aces help.
    int BotCardValue(int c, int suit, int trumpSuit)
    {
        int rank = c % 6;
        int cs = c / 6;
        if (rank == 2 && cs == trumpSuit) return 30;
        if (rank == 2 && cs == (trumpSuit ^ 1)) return 25;
        if (cs == trumpSuit)
        {
            if (rank == 5) return 20;
            if (rank == 4) return 17;
            if (rank == 3) return 15;
            if (rank == 1) return 13;
            return 12;
        }
        if (rank == 5) return 10;
        if (rank == 4) return 3;
        return 0;
    }

    bool BotHoldsRight(int s, int suit)
    {
        for (int k = 0; k < HandSize; k++) if (hands[s * HandSize + k] == suit * 6 + 2) return true;
        return false;
    }

    int BotEffSuit(int c, int trumpSuit)
    {
        if (c % 6 == 2 && c / 6 == (trumpSuit ^ 1)) return trumpSuit;
        return c / 6;
    }

    int BotDiscardChoice(int s, int trumpSuit)
    {
        return BotDiscardChoiceWithExtra(s, trumpSuit, -1);
    }

    /// The dealer's discard: the lowest off-suit card, preferring one that leaves a void; trump only if forced.
    int BotDiscardChoiceWithExtra(int s, int trumpSuit, int extra)
    {
        int[] counts = new int[4];
        for (int k = 0; k <= HandSize; k++)
        {
            int c = k < HandSize ? hands[s * HandSize + k] : extra;
            if (c >= 0) counts[BotEffSuit(c, trumpSuit)]++;
        }
        int best = -1;
        int bestV = 0;
        for (int k = 0; k <= HandSize; k++)
        {
            int c = k < HandSize ? hands[s * HandSize + k] : extra;
            if (c < 0) continue;
            int eff = BotEffSuit(c, trumpSuit);
            int v;
            if (eff == trumpSuit) v = 100 + BotCardValue(c, trumpSuit, trumpSuit);
            else
            {
                v = c % 6;
                if (c % 6 == 5) v += 20;
                if (counts[eff] == 1) v -= 5;
            }
            if (best < 0 || v < bestV) { best = c; bestV = v; }
        }
        return best;
    }

    int BotChoosePlay(int s)
    {
        bool leading = cardsInTrick == 0 || TrickComplete();
        return leading ? BotLead(s) : BotFollow(s);
    }

    int BotLead(int s)
    {
        bool weMade = maker % 2 == s % 2;
        int hiTrump = -1, loTrump = -1, bossOff = -1, loOff = -1;
        int trumps = 0;
        for (int k = 0; k < HandSize; k++)
        {
            int c = hands[s * HandSize + k];
            if (c < 0) continue;
            if (EffectiveSuit(c) == trump)
            {
                trumps++;
                if (hiTrump < 0 || Strength(c, trump) > Strength(hiTrump, trump)) hiTrump = c;
                if (loTrump < 0 || Strength(c, trump) < Strength(loTrump, trump)) loTrump = c;
            }
            else
            {
                if (BotIsBoss(s, c) && (bossOff < 0 || c % 6 > bossOff % 6)) bossOff = c;
                if (loOff < 0 || c % 6 < loOff % 6) loOff = c;
            }
        }
        bool trumpOut = BotTrumpOutstanding(s);
        if (weMade && hiTrump >= 0 && trumpOut)
        {
            // Pull trump with a boss trump, or lead trump to a partner who called it.
            if (BotIsBoss(s, hiTrump)) return hiTrump;
            if (maker != s && sittingOut < 0) return loTrump;
            if (trumps >= 3) return hiTrump;
        }
        if (bossOff >= 0) return bossOff;
        if (loOff >= 0) return loOff;
        return BotIsBoss(s, hiTrump) ? hiTrump : loTrump;
    }

    int BotFollow(int s)
    {
        int led = EffectiveSuit(trick[leadSeat]);
        int winSeat = -1;
        int winStrength = -1;
        for (int i = 0; i < 4; i++)
        {
            if (trick[i] < 0) continue;
            int v = Strength(trick[i], led);
            if (v > winStrength) { winStrength = v; winSeat = i; }
        }
        bool last = cardsInTrick == PlayersInHand() - 1;
        bool partnerWinning = winSeat % 2 == s % 2;

        int lowest = -1, lowestWinner = -1, bossWinner = -1;
        for (int k = 0; k < HandSize; k++)
        {
            int c = hands[s * HandSize + k];
            if (c < 0 || !IsLegal(s, c)) continue;
            int v = Strength(c, led);
            if (lowest < 0 || BotThrowValue(c, led) < BotThrowValue(lowest, led)) lowest = c;
            if (v > winStrength)
            {
                if (lowestWinner < 0 || v < Strength(lowestWinner, led)) lowestWinner = c;
                if (BotIsBoss(s, c) && (bossWinner < 0 || v < Strength(bossWinner, led))) bossWinner = c;
            }
        }

        // Partner has it: throw low unless an opponent still to play could easily beat them.
        if (partnerWinning && (last || BotIsBoss(s, trick[winSeat]) || winStrength >= 50)) return lowest;
        if (lowestWinner < 0) return lowest;
        if (last) return lowestWinner;
        // Third hand high: win with a card nobody left can beat when we have one.
        if (bossWinner >= 0) return bossWinner;
        return partnerWinning ? lowest : lowestWinner;
    }

    /// Lower = better to throw away: low off-suit cards first, aces and trump last.
    int BotThrowValue(int c, int led)
    {
        int eff = EffectiveSuit(c);
        if (eff == trump) return 100 + Strength(c, led);
        int v = c % 6;
        if (c % 6 == 5) v += 10;
        if (eff == led) v += 20;
        return v;
    }

    /// True if no card that could still be out (not played, not in our hand) beats c in its own suit.
    bool BotIsBoss(int s, int c)
    {
        if (c < 0) return false;
        int eff = EffectiveSuit(c);
        int mine = Strength(c, eff);
        for (int o = 0; o < 24; o++)
        {
            if (o == c || (playedMask & (1 << o)) != 0 || EffectiveSuit(o) != eff) continue;
            if (SlotOf(s, o) >= 0) continue;
            if (Strength(o, eff) > mine) return false;
        }
        return true;
    }

    /// True if any trump not in our hand hasn't been played yet.
    bool BotTrumpOutstanding(int s)
    {
        for (int o = 0; o < 24; o++)
        {
            if ((playedMask & (1 << o)) != 0 || EffectiveSuit(o) != trump) continue;
            if (SlotOf(s, o) < 0) return true;
        }
        return false;
    }

    // ================= readable table =================

    /// Local only: other players' played cards, the upcard and the trump marker turn so they read upright for
    /// this viewer and tilt up. Your own played card stays flat. Purely visual, so it isn't synced.
    void UpdateFacing()
    {
        if (trickSpots == null || trickSpots.Length < 4) return;
        if (!homesSaved)
        {
            for (int s = 0; s < 4; s++)
            {
                if (trickSpots[s] == null) continue;
                trickHome[s] = trickSpots[s].localPosition;
                trickHomeRot[s] = trickSpots[s].localRotation;
            }
            if (upcardSpot != null) { upHome = upcardSpot.localPosition; upHomeRot = upcardSpot.localRotation; }
            if (trumpSpot != null) { trumpHome = trumpSpot.localPosition; trumpHomeRot = trumpSpot.localRotation; }
            homesSaved = true;
        }

        int me = LocalSeat();
        Vector3 viewer;
        if (me >= 0 && trickSpots[me] != null)
        {
            // Seated: look from about where that seat's player sits, so the cards hold still.
            Vector3 centre = transform.position;
            Vector3 out1 = trickSpots[me].parent.TransformPoint(trickHome[me]) - centre;
            out1.y = 0f;
            viewer = centre + out1 * 4.5f;
        }
        else
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            viewer = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        }

        for (int s = 0; s < 4; s++)
            FaceViewer(trickSpots[s], trickHome[s], trickHomeRot[s], faceCardsToViewer && s != me, viewer, cardTilt, cardScale, 0.045f);
        FaceViewer(upcardSpot, upHome, upHomeRot, faceCardsToViewer, viewer, cardTilt, cardScale, 0.045f);
        FaceViewer(trumpSpot, trumpHome, trumpHomeRot, faceCardsToViewer, viewer, cardTilt, 1f, 0.05f);
    }

    /// Turns p (at its home spot) so its +Z, the top of the card, points away from the viewer, tilts the far
    /// edge up and raises it by enough (halfDepth = distance from the pivot to the near edge) to clear the table.
    void FaceViewer(Transform p, Vector3 home, Quaternion homeRot, bool face, Vector3 viewer, float tilt, float scale, float halfDepth)
    {
        if (p == null) return;
        if (!face)
        {
            p.localPosition = home;
            p.localRotation = homeRot;
            p.localScale = Vector3.one;
            return;
        }
        Vector3 homeWorld = p.parent.TransformPoint(home);
        Vector3 d = viewer - homeWorld;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) return;
        p.rotation = Quaternion.LookRotation(-d) * Quaternion.Euler(-tilt, 0f, 0f);
        p.position = homeWorld + Vector3.up * (halfDepth * scale * Mathf.Sin(tilt * Mathf.Deg2Rad) + 0.005f);
        p.localScale = Vector3.one * scale;
    }

    // ================= players =================

    bool IsRunning()
    {
        return phase != PhaseLobby && phase != PhaseGameOver;
    }

    bool IsBot(int s)
    {
        return seatPlayers[s] == BotSeat;
    }

    bool ControlsSeat(int s)
    {
        if (IsBot(s)) return false;
        if (debugControlAllSeats) return true;
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) && seatPlayers[s] == local.playerId;
    }

    int FindSeat(int pid)
    {
        for (int s = 0; s < 4; s++) if (seatPlayers[s] == pid) return s;
        return -1;
    }

    int LocalSeat()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) ? FindSeat(local.playerId) : -1;
    }

    /// Seat the local player is bidding for, or -1 (with a message) if it isn't their turn.
    int BiddingSeat()
    {
        if (phase != PhaseBid1 && phase != PhaseBid2) { Hint("Bidding is over."); return -1; }
        if (!ControlsSeat(turn)) { Hint("It's " + SeatName(turn) + "'s turn."); return -1; }
        return turn;
    }

    int SeatedCount()
    {
        int n = 0;
        for (int s = 0; s < 4; s++) if (seatPlayers[s] != -1) n++;
        return n;
    }

    string SeatName(int s)
    {
        if (s < 0 || s > 3) return "?";
        if (IsBot(s)) return "Bot " + seatNames[s];
        VRCPlayerApi p = seatPlayers[s] >= 0 ? VRCPlayerApi.GetPlayerById(seatPlayers[s]) : null;
        if (Utilities.IsValid(p)) return p.displayName;
        return seatNames[s];
    }

    string TeamName(int team)
    {
        return team == 0 ? "Team " + SeatName(0) + "/" + SeatName(2) : "Team " + SeatName(1) + "/" + SeatName(3);
    }

    string CardName(int c)
    {
        int r = c % 6;
        string rank = r == 0 ? "9" : r == 1 ? "10" : r == 2 ? "J" : r == 3 ? "Q" : r == 4 ? "K" : "A";
        return rank + suitSymbols[c / 6];
    }

    // ================= sync + display =================

    void Commit()
    {
        RequestSerialization();
        Refresh();
        ScheduleBots();
    }

    void Hint(string msg)
    {
        Refresh();
        if (statusText != null) statusText.text = "<color=#ffcc66>" + msg + "</color>\n" + statusText.text;
    }

    void Refresh()
    {
        bool inHand = phase != PhaseLobby;
        bool reveal = phase == PhaseHandOver || phase == PhaseGameOver;

        for (int s = 0; s < 4; s++)
        {
            bool mine = ControlsSeat(s);
            bool myTurn = (phase == PhasePlay && s == turn && mine) || (phase == PhaseDiscard && s == dealer && mine);

            for (int k = 0; k < HandSize; k++)
            {
                int idx = s * HandSize + k;
                if (handCards == null || idx >= handCards.Length || handCards[idx] == null) continue;
                int c = inHand ? hands[idx] : -1;
                if (c < 0) handCards[idx].Hide();
                else if (mine || reveal) handCards[idx].ShowFace(c, myTurn && (phase == PhaseDiscard || CanPlay(s, c)));
                else handCards[idx].ShowBack();
            }

            if (trickCards != null && s < trickCards.Length && trickCards[s] != null)
            {
                int t = inHand ? trick[s] : -1;
                if (t >= 0) trickCards[s].ShowFace(t, TrickComplete() && s == TrickWinnerOrNone());
                else trickCards[s].Hide();
            }

            if (seatLabels != null && s < seatLabels.Length && seatLabels[s] != null)
            {
                string name = seatPlayers[s] != -1 ? SeatName(s) : seatNames[s] + " (empty)";
                if (inHand && s == dealer) name += " [D]";
                if (inHand && s == maker) name += " [M]";
                if (inHand && s == sittingOut) name += " (sitting out)";
                bool active = (phase >= PhaseBid1 && phase <= PhasePlay) && s == (phase == PhaseDiscard ? dealer : turn);
                seatLabels[s].text = active ? "<color=#ffdd33>> " + name + " <</color>" : name;
            }
        }

        if (upcard != null)
        {
            if (phase == PhaseBid1 && upcardId >= 0) upcard.ShowFace(upcardId, false);
            else if (phase == PhaseBid2) upcard.ShowBack();
            else upcard.Hide();
        }

        if (trumpText != null)
        {
            bool show = inHand && trump >= 0;
            trumpText.text = show ? suitSymbols[trump] : "";
            trumpText.color = (trump == 2 || trump == 3) ? new Color(0.9f, 0.15f, 0.15f) : new Color(0.05f, 0.05f, 0.05f);
        }

        UpdateButtons();
        if (statusText != null) statusText.text = BuildStatus();
        PlayEventSounds();
    }

    // ================= sounds =================

    /// Owner: marks a sound event; every client plays it once when the state arrives.
    void Sfx(int kind)
    {
        sfxKind = kind;
        sfxSerial++;
    }

    void PlaySound(AudioClip clip, float pitch)
    {
        if (sfxSource == null || clip == null) return;
        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, soundVolume);
    }

    void PlayCardSound()
    {
        if (cardSounds == null || cardSounds.Length == 0) return;
        PlaySound(cardSounds[Random.Range(0, cardSounds.Length)], 1f + Random.Range(-cardPitchVariation, cardPitchVariation));
    }

    int CardsPlayed()
    {
        int n = 0;
        for (int m = playedMask; m != 0; m &= m - 1) n++;
        return n;
    }

    /// Every client: a card sound when a new card hits the table, the latest synced event, and a local
    /// "your turn" cue. The first refresh only records the state, so joining mid-game is silent.
    void PlayEventSounds()
    {
        int played = CardsPlayed();
        int turnKey = (phase >= PhaseBid1 && phase <= PhasePlay) ? phase * 4 + (phase == PhaseDiscard ? dealer : turn) : -1;
        if (!soundsPrimed)
        {
            // Wait for real state: a late joiner's Start() refresh still holds the defaults.
            if (!gotState && !Networking.IsOwner(gameObject)) return;
            soundsPrimed = true;
            heardSfx = sfxSerial;
            heardCards = played;
            heardTurnKey = turnKey;
            return;
        }

        if (played > heardCards) PlayCardSound();
        heardCards = played;

        if (sfxSerial != heardSfx)
        {
            heardSfx = sfxSerial;
            if (sfxKind == SfxDeal) PlaySound(dealSound, 1f);
            else if (sfxKind == SfxPass) PlaySound(passSound, 1f);
            else if (sfxKind == SfxTrump) PlaySound(trumpCalledSound, 1f);
            else if (sfxKind == SfxAlone) PlaySound(goingAloneSound != null ? goingAloneSound : trumpCalledSound, 1f);
            else if (sfxKind == SfxDiscard) PlayCardSound();
            else if (sfxKind == SfxTrick) PlaySound(trickWonSound, 1f);
            else if (sfxKind == SfxMadeIt) PlaySound(madeItSound, 1f);
            else if (sfxKind == SfxMarch) PlaySound(marchSound != null ? marchSound : madeItSound, 1f);
            else if (sfxKind == SfxEuchred) PlaySound(euchredSound, 1f);
            else if (sfxKind == SfxGameWon) PlaySound(gameWonSound, 1f);
        }

        if (turnKey != heardTurnKey)
        {
            heardTurnKey = turnKey;
            if (turnKey >= 0 && !debugControlAllSeats && ControlsSeat(turnKey % 4)) PlaySound(yourTurnSound, 1f);
        }
    }

    // ================= context buttons =================

    /// Called by a context button. Runs whatever that slot is showing for the local player.
    public void OnActionButton(int seat, int slot)
    {
        if (seat < 0 || seat > 3 || slot < 0 || slot >= ButtonSlots || !ControlsSeat(seat)) return;
        int code = slotCode[seat * ButtonSlots + slot];
        int arg = slotArg[seat * ButtonSlots + slot];
        if (code == BtnOrder) { localAlone = false; ActOrderUp(); }
        else if (code == BtnOrderAlone) { localAlone = true; ActOrderUp(); }
        else if (code == BtnPass) ActPass();
        else if (code == BtnSuit) CallSuit(arg);
        else if (code == BtnAloneToggle) ActToggleAlone();
        else if (code == BtnStart) ActStart();
        else if (code == BtnNext) ActNextHand();
        else if (code == BtnReset) ActReset();
    }

    void UpdateButtons()
    {
        if (actionButtons == null) return;
        for (int s = 0; s < 4; s++)
        {
            slotFill = 0;
            if (ControlsSeat(s)) AddSeatButtons(s);
            for (int k = slotFill; k < ButtonSlots; k++) SetSlot(s, k, BtnNone, 0, "");
        }
    }

    void AddSeatButtons(int s)
    {
        if (phase == PhaseLobby) { AddButton(s, BtnStart, 0, "Start"); return; }
        if (phase == PhaseGameOver) { AddButton(s, BtnStart, 0, "New Game"); return; }
        if (phase == PhaseHandOver)
        {
            AddButton(s, BtnNext, 0, "Next Hand");
            AddButton(s, BtnReset, 0, "Reset");
            return;
        }
        if (s != turn) return;
        if (phase == PhaseBid1)
        {
            AddButton(s, BtnOrder, 0, s == dealer ? "Pick Up" : "Order Up");
            AddButton(s, BtnOrderAlone, 0, "Go Alone");
            AddButton(s, BtnPass, 0, "Pass");
        }
        else if (phase == PhaseBid2)
        {
            for (int suit = 0; suit < 4; suit++) if (suit != turnedDown) AddButton(s, BtnSuit, suit, suitNames[suit]);
            if (!(stickTheDealer && s == dealer)) AddButton(s, BtnPass, 0, "Pass");
            AddButton(s, BtnAloneToggle, 0, localAlone ? "Alone: On" : "Alone: Off");
        }
    }

    void AddButton(int s, int code, int arg, string text)
    {
        if (slotFill >= ButtonSlots) return;
        SetSlot(s, slotFill, code, arg, text);
        slotFill++;
    }

    void SetSlot(int s, int k, int code, int arg, string text)
    {
        int i = s * ButtonSlots + k;
        slotCode[i] = code;
        slotArg[i] = arg;
        if (i < actionButtons.Length && actionButtons[i] != null) actionButtons[i].Show(text);
    }

    int TrickWinnerOrNone()
    {
        return leadSeat >= 0 && cardsInTrick > 0 ? TrickWinner() : -1;
    }

    string BuildStatus()
    {
        string s = "<b>EUCHRE</b>   " + TeamName(0) + ": " + score[0] + "   " + TeamName(1) + ": " + score[1] + "\n";
        if (phase != PhaseLobby && trump >= 0)
        {
            s += "Trump: " + suitNames[trump] + " " + suitSymbols[trump] + " (called by " + SeatName(maker) + (goingAlone ? ", alone" : "") + ")   Tricks " + tricksWon[0] + "-" + tricksWon[1] + "\n";
        }

        if (phase == PhaseLobby) s += "Click Sit (or Bot) at each seat, then Start. (" + SeatedCount() + "/4 seated)";
        else if (phase == PhaseBid1) s += SeatName(turn) + ": Order Up or Pass on the " + CardName(upcardId) + ".";
        else if (phase == PhaseBid2) s += SeatName(turn) + ": name a suit (not " + suitNames[turnedDown] + ") or Pass" + (turn == dealer ? (stickTheDealer ? " (dealer must call)." : " (dealer passing redeals).") : ".");
        else if (phase == PhaseDiscard) s += SeatName(dealer) + ": click a card to discard.";
        else if (phase == PhasePlay) s += SeatName(turn) + " to play.";
        else if (phase == PhaseHandOver) s += nextHandSeconds > 0f ? "Next hand in " + SecondsLeft() + "s (or press Next Hand)." : "Press Next Hand.";
        else if (phase == PhaseGameOver) s += "Game over. Press Start for a new game.";

        if (!string.IsNullOrEmpty(lastEvent)) s += "\n<size=80%>" + lastEvent + "</size>";
        if (localAlone) s += "\n<color=#66ccff>Alone is ON for your next bid.</color>";
        if (debugControlAllSeats) s += "\n<size=70%><color=#ff8888>Debug: controlling all seats</color></size>";
        return s;
    }

    int SecondsLeft()
    {
        int ms = timerDeadline - Networking.GetServerTimeInMilliseconds();
        return ms <= 0 ? 0 : (ms + 999) / 1000;
    }
}
