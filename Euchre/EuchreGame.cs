using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// Networked 4-player euchre. Whoever acts takes ownership, updates the synced state and serializes it;
/// every client rebuilds the table visuals from that state.
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

    [Header("Testing")]
    [Tooltip("Lets the local player act for every seat and start without 4 players.")]
    public bool debugControlAllSeats = false;
    public int pointsToWin = 10;

    const int PhaseLobby = 0;
    const int PhaseBid1 = 1;        // order up / pass on the upcard
    const int PhaseBid2 = 2;        // name a different suit / pass
    const int PhaseDiscard = 3;     // dealer discards after picking up
    const int PhasePlay = 4;
    const int PhaseHandOver = 5;
    const int PhaseGameOver = 6;

    const int HandSize = 6;

    [UdonSynced] int[] seatPlayers = { -1, -1, -1, -1 };
    [UdonSynced] int phase = PhaseLobby;
    [UdonSynced] int dealer = 0;
    [UdonSynced] int turn = 0;
    [UdonSynced] int[] hands = new int[24];
    [UdonSynced] int upcardId = -1;
    [UdonSynced] int trump = -1;
    [UdonSynced] int maker = -1;        // seat that named trump
    [UdonSynced] bool goingAlone = false;
    [UdonSynced] int sittingOut = -1;   // partner of a lone maker
    [UdonSynced] int[] trick = { -1, -1, -1, -1 };
    [UdonSynced] int leadSeat = -1;
    [UdonSynced] int cardsInTrick = 0;
    [UdonSynced] int[] tricksWon = new int[2];
    [UdonSynced] int[] score = new int[2];
    [UdonSynced] string lastEvent = "";

    bool localAlone = false; // the local player's "Alone" toggle, applied when they order up / call

    string[] suitNames = { "Clubs", "Spades", "Hearts", "Diamonds" };
    string[] seatNames = { "South", "West", "North", "East" };

    void Start()
    {
        for (int i = 0; i < hands.Length; i++) hands[i] = -1;
        Refresh();
    }

    void Update()
    {
        if (statusAnchor == null) return;
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;
        Vector3 head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        Vector3 dir = statusAnchor.position - head;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) statusAnchor.rotation = Quaternion.LookRotation(dir);
    }

    public override void OnDeserialization()
    {
        Refresh();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!Networking.IsOwner(gameObject) || player == null) return;
        bool changed = false;
        for (int s = 0; s < 4; s++)
        {
            if (seatPlayers[s] == player.playerId)
            {
                seatPlayers[s] = -1;
                changed = true;
            }
        }
        if (!changed) return;
        if (phase != PhaseLobby && phase != PhaseGameOver && !debugControlAllSeats)
        {
            phase = PhaseLobby;
            lastEvent = "A player left. Game stopped.";
        }
        Commit();
    }

    // ---------------- seats ----------------

    public void ToggleSeat(int s)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local) || s < 0 || s > 3) return;
        int me = local.playerId;

        if (seatPlayers[s] == me)
        {
            TakeOwnership();
            seatPlayers[s] = -1;
            lastEvent = local.displayName + " stood up.";
            if (phase != PhaseLobby && phase != PhaseGameOver && !debugControlAllSeats)
            {
                phase = PhaseLobby;
                lastEvent += " Game stopped.";
            }
            Commit();
            return;
        }
        if (seatPlayers[s] != -1)
        {
            ShowLocal("That seat is taken.");
            return;
        }
        if (phase != PhaseLobby && phase != PhaseGameOver && !debugControlAllSeats)
        {
            ShowLocal("Wait for the current game to end.");
            return;
        }

        TakeOwnership();
        for (int i = 0; i < 4; i++) if (seatPlayers[i] == me) seatPlayers[i] = -1;
        seatPlayers[s] = me;
        lastEvent = local.displayName + " sat at " + seatNames[s] + ".";
        Commit();
    }

    // ---------------- buttons ----------------

    public void ActStart()
    {
        if (phase != PhaseLobby && phase != PhaseGameOver)
        {
            ShowLocal("A game is already running. Use Reset to stop it.");
            return;
        }
        if (!IsLocallySeated()) { ShowLocal("Sit down first."); return; }
        if (!debugControlAllSeats && SeatedCount() < 4)
        {
            ShowLocal("Need 4 players to start.");
            return;
        }
        TakeOwnership();
        score[0] = 0;
        score[1] = 0;
        dealer = Random.Range(0, 4);
        Deal();
        lastEvent = "New game! " + SeatName(dealer) + " deals.";
        Commit();
    }

    public void ActNextHand()
    {
        if (phase != PhaseHandOver) { ShowLocal("The hand isn't over yet."); return; }
        if (!IsLocallySeated()) { ShowLocal("Sit down first."); return; }
        TakeOwnership();
        dealer = (dealer + 1) % 4;
        Deal();
        lastEvent = SeatName(dealer) + " deals.";
        Commit();
    }

    public void ActReset()
    {
        if (!IsLocallySeated()) { ShowLocal("Sit down first."); return; }
        TakeOwnership();
        phase = PhaseLobby;
        ClearHand();
        score[0] = 0;
        score[1] = 0;
        lastEvent = "Game reset.";
        Commit();
    }

    public void ActToggleAlone()
    {
        localAlone = !localAlone;
        Refresh();
    }

    public void ActPass()
    {
        int s = ActingSeat();
        if (s < 0) return;
        if (phase == PhaseBid1)
        {
            TakeOwnership();
            lastEvent = SeatName(s) + " passed.";
            if (s == dealer)
            {
                phase = PhaseBid2;
                turn = NextSeat(dealer);
                lastEvent += " Upcard turned down.";
            }
            else turn = NextSeat(s);
            Commit();
        }
        else if (phase == PhaseBid2)
        {
            TakeOwnership();
            if (s == dealer)
            {
                dealer = (dealer + 1) % 4;
                Deal();
                lastEvent = "Everyone passed. " + SeatName(dealer) + " redeals.";
            }
            else
            {
                lastEvent = SeatName(s) + " passed.";
                turn = NextSeat(s);
            }
            Commit();
        }
        else ShowLocal("You can't pass right now.");
    }

    public void ActOrderUp()
    {
        int s = ActingSeat();
        if (s < 0) return;
        if (phase != PhaseBid1) { ShowLocal("Ordering up is only allowed on the first round."); return; }
        TakeOwnership();
        SetTrump(s, upcardId / 6);
        lastEvent = SeatName(s) + (s == dealer ? " picked it up" : " ordered it up") + ": " + suitNames[trump] + (goingAlone ? ", going alone!" : ".");

        if (goingAlone && sittingOut == dealer)
        {
            // Dealer is the lone maker's partner and sits out, so the upcard stays down.
            BeginPlay();
        }
        else
        {
            hands[dealer * HandSize + 5] = upcardId;
            phase = PhaseDiscard;
            turn = dealer;
            lastEvent += " " + SeatName(dealer) + " must discard.";
        }
        upcardId = -1;
        Commit();
    }

    public void ActCallClubs() { CallSuit(0); }
    public void ActCallSpades() { CallSuit(1); }
    public void ActCallHearts() { CallSuit(2); }
    public void ActCallDiamonds() { CallSuit(3); }

    void CallSuit(int suit)
    {
        int s = ActingSeat();
        if (s < 0) return;
        if (phase != PhaseBid2) { ShowLocal("You can only name a suit on the second round."); return; }
        if (suit == upcardId / 6) { ShowLocal(suitNames[suit] + " was turned down. Pick another suit."); return; }
        TakeOwnership();
        SetTrump(s, suit);
        lastEvent = SeatName(s) + " called " + suitNames[suit] + (goingAlone ? ", going alone!" : ".");
        upcardId = -1;
        BeginPlay();
        Commit();
    }

    // ---------------- cards ----------------

    public void OnCardClicked(int s, int slot)
    {
        if (!ControlsSeat(s)) { ShowLocal("Those aren't your cards."); return; }
        int idx = s * HandSize + slot;
        int card = hands[idx];
        if (card < 0) return;

        if (phase == PhaseDiscard)
        {
            if (s != dealer) { ShowLocal("Waiting for the dealer to discard."); return; }
            TakeOwnership();
            hands[idx] = -1;
            lastEvent = SeatName(dealer) + " discarded.";
            BeginPlay();
            Commit();
            return;
        }

        if (phase != PhasePlay) { ShowLocal("It's not time to play a card."); return; }
        if (s != turn) { ShowLocal("It's " + SeatName(turn) + "'s turn."); return; }

        if (!CanPlay(s, card)) { ShowLocal("You must follow " + suitNames[EffectiveSuit(trick[leadSeat])] + "."); return; }

        TakeOwnership();
        // A finished trick stays on the table until its winner leads the next card.
        if (TrickComplete()) ClearTrick();
        if (cardsInTrick == 0) leadSeat = s;
        trick[s] = card;
        hands[idx] = -1;
        cardsInTrick++;

        if (cardsInTrick < PlayersInHand())
        {
            turn = NextSeat(s);
            lastEvent = SeatName(s) + " played.";
        }
        else FinishTrick();
        Commit();
    }

    // ---------------- rules ----------------

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
    }

    void ClearHand()
    {
        for (int i = 0; i < hands.Length; i++) hands[i] = -1;
        for (int i = 0; i < 4; i++) trick[i] = -1;
        upcardId = -1;
        trump = -1;
        maker = -1;
        goingAlone = false;
        sittingOut = -1;
        leadSeat = -1;
        cardsInTrick = 0;
        tricksWon[0] = 0;
        tricksWon[1] = 0;
    }

    void SetTrump(int s, int suit)
    {
        trump = suit;
        maker = s;
        goingAlone = IsLocalAloneFor(s);
        sittingOut = goingAlone ? (s + 2) % 4 : -1;
        localAlone = false;
    }

    void BeginPlay()
    {
        phase = PhasePlay;
        for (int i = 0; i < 4; i++) trick[i] = -1;
        cardsInTrick = 0;
        leadSeat = -1;
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
        int winner = leadSeat;
        int best = Strength(trick[leadSeat]);
        for (int i = 0; i < 4; i++)
        {
            if (trick[i] < 0) continue;
            int v = Strength(trick[i]);
            if (v > best) { best = v; winner = i; }
        }
        tricksWon[winner % 2]++;
        turn = winner;
        lastEvent = SeatName(winner) + " took the trick.";

        if (tricksWon[0] + tricksWon[1] >= 5) ScoreHand();
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
        lastEvent = TeamName(makers) + " " + result + ". " + TeamName(team) + " +" + points + ".";

        if (score[team] >= pointsToWin)
        {
            phase = PhaseGameOver;
            lastEvent += " " + TeamName(team) + " win the game!";
        }
        else phase = PhaseHandOver;
    }

    /// Suit a card counts as once trump is known (the left bower belongs to trump).
    int EffectiveSuit(int card)
    {
        int suit = card / 6;
        if (trump >= 0 && card % 6 == 2 && suit == (trump ^ 1)) return trump;
        return suit;
    }

    int Strength(int card)
    {
        int suit = card / 6;
        int rank = card % 6;
        if (rank == 2 && suit == trump) return 100;          // right bower
        if (rank == 2 && suit == (trump ^ 1)) return 99;     // left bower
        int eff = EffectiveSuit(card);
        if (eff == trump) return 50 + rank;
        if (eff == EffectiveSuit(trick[leadSeat])) return 10 + rank;
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

    // ---------------- players ----------------

    bool ControlsSeat(int s)
    {
        if (debugControlAllSeats) return true;
        VRCPlayerApi local = Networking.LocalPlayer;
        return Utilities.IsValid(local) && seatPlayers[s] == local.playerId;
    }

    int LocalSeat()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return -1;
        for (int s = 0; s < 4; s++) if (seatPlayers[s] == local.playerId) return s;
        return -1;
    }

    bool IsLocallySeated()
    {
        return debugControlAllSeats || LocalSeat() >= 0;
    }

    /// Seat the local player is acting for during bidding, or -1 (with a message) if it isn't their turn.
    int ActingSeat()
    {
        if (phase != PhaseBid1 && phase != PhaseBid2) { ShowLocal("Bidding is over."); return -1; }
        if (!ControlsSeat(turn)) { ShowLocal("It's " + SeatName(turn) + "'s turn."); return -1; }
        return turn;
    }

    bool IsLocalAloneFor(int s)
    {
        return localAlone && ControlsSeat(s);
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
        VRCPlayerApi p = seatPlayers[s] >= 0 ? VRCPlayerApi.GetPlayerById(seatPlayers[s]) : null;
        if (Utilities.IsValid(p)) return p.displayName;
        return seatNames[s];
    }

    string TeamName(int team)
    {
        return team == 0 ? "Team " + seatNames[0] + "/" + seatNames[2] : "Team " + seatNames[1] + "/" + seatNames[3];
    }

    // ---------------- sync + display ----------------

    void TakeOwnership()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (Utilities.IsValid(local) && !Networking.IsOwner(gameObject)) Networking.SetOwner(local, gameObject);
    }

    void Commit()
    {
        RequestSerialization();
        Refresh();
    }

    void ShowLocal(string msg)
    {
        Refresh();
        if (statusText != null) statusText.text = "<color=#ffcc66>" + msg + "</color>\n" + statusText.text;
    }

    void Refresh()
    {
        bool inHand = phase != PhaseLobby;

        for (int s = 0; s < 4; s++)
        {
            bool mine = ControlsSeat(s);
            bool myTurn = phase == PhasePlay && s == turn && mine;

            for (int k = 0; k < HandSize; k++)
            {
                int idx = s * HandSize + k;
                if (handCards == null || idx >= handCards.Length || handCards[idx] == null) continue;
                int c = inHand ? hands[idx] : -1;
                if (c < 0) handCards[idx].Hide();
                else if (mine) handCards[idx].ShowFace(c, myTurn && CanPlay(s, c));
                else handCards[idx].ShowBack();
            }

            if (trickCards != null && s < trickCards.Length && trickCards[s] != null)
            {
                int t = inHand ? trick[s] : -1;
                if (t >= 0) trickCards[s].ShowFace(t, false);
                else trickCards[s].Hide();
            }

            if (seatLabels != null && s < seatLabels.Length && seatLabels[s] != null)
            {
                string name = seatPlayers[s] >= 0 ? SeatName(s) : seatNames[s] + " (empty)";
                if (inHand && s == dealer) name += " [D]";
                if (inHand && s == sittingOut) name += " (sitting out)";
                bool active = (phase >= PhaseBid1 && phase <= PhasePlay) && s == turn;
                seatLabels[s].text = active ? "<color=#ffdd33>> " + name + " <</color>" : name;
            }
        }

        if (upcard != null)
        {
            if (phase == PhaseBid1 && upcardId >= 0) upcard.ShowFace(upcardId, false);
            else if (phase == PhaseBid2) upcard.ShowBack();
            else upcard.Hide();
        }

        if (statusText != null) statusText.text = BuildStatus();
    }

    string BuildStatus()
    {
        string s = "<b>EUCHRE</b>   " + TeamName(0) + ": " + score[0] + "   " + TeamName(1) + ": " + score[1] + "\n";
        if (phase != PhaseLobby && trump >= 0)
        {
            s += "Trump: " + suitNames[trump] + " (called by " + SeatName(maker) + (goingAlone ? ", alone" : "") + ")   Tricks " + tricksWon[0] + "-" + tricksWon[1] + "\n";
        }

        if (phase == PhaseLobby) s += "Click a yellow Sit button, then Start. (" + SeatedCount() + "/4 seated)";
        else if (phase == PhaseBid1) s += SeatName(turn) + ": Order Up or Pass on the " + suitNames[upcardId / 6] + ".";
        else if (phase == PhaseBid2) s += SeatName(turn) + ": name a suit or Pass" + (turn == dealer ? " (dealer passing redeals)." : ".");
        else if (phase == PhaseDiscard) s += SeatName(dealer) + ": click a card to discard.";
        else if (phase == PhasePlay) s += SeatName(turn) + " to play.";
        else if (phase == PhaseHandOver) s += "Press Next Hand.";
        else if (phase == PhaseGameOver) s += "Game over. Press Start for a new game.";

        if (!string.IsNullOrEmpty(lastEvent)) s += "\n<size=80%>" + lastEvent + "</size>";
        if (localAlone) s += "\n<color=#66ccff>Alone is ON for your next bid.</color>";
        if (debugControlAllSeats) s += "\n<size=70%><color=#ff8888>Debug: controlling all seats</color></size>";
        return s;
    }
}
