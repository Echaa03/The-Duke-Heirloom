// ================================================================
//  THE DUKE HEIRLOOM — Prologue Act 1 · Scene 1.2 · Inspection Mode
//  INCLUDEd by prologue.ink, so both share the same story + variables.
// ================================================================
//
//  HOW IT WORKS
//  - prologue.ink reaches  #inspect: police_report  -> the inspection opens.
//  - Clicking a clue runs the knot with the same name as the hotspot:
//      HS_01_A  ->  inspect_01_A
//  - A clue that was already read says nothing (it just zooms in again).
//  - Lines use the same format as prologue.ink:   Name : text
//
//  TAGS / VARIABLES
//    #evidence:TIMELINE        evidence found (small "Evidence noted" pop-up)
//    ~ inspection_done = true  this inspection is finished -> the document
//                              closes and prologue.ink continues after the
//                              #inspect line (the game resets it to false)
//
//  TWO INSPECTIONS (INSPECTION-02 / phone log is skipped for now)
//    Phase 1  #inspect: police_report   (INSPECTION-01)
//      done when TIMELINE + LOCATION + 01_D are found
//        TIMELINE = 01_A + 01_B   (friend vs. family statement)
//        LOCATION = 01_C          (note 4.1: last ping 23:40, Old Shopping District)
//        01_D     = note 4.2      (the mirror purchase)
//    Phase 2  #inspect: bank_statement  (INSPECTION-03)
//      done when MIRROR is found
//        MIRROR   = 03_A + (01_E or 03_B) (the purchase + "cashier input error")
// ================================================================

VAR seen_01_A = false
VAR seen_01_B = false
VAR seen_01_C = false
VAR seen_01_D = false
VAR seen_01_E = false
VAR seen_01_F = false
VAR seen_01_G = false
VAR seen_02_A = false
VAR seen_02_B = false
VAR seen_02_C = false
VAR seen_02_D = false
VAR seen_02_E = false
VAR seen_03_A = false
VAR seen_03_B = false
VAR seen_03_C = false

VAR ev_timeline = false
VAR ev_location = false
VAR ev_mirror = false
VAR said_two_hours = false
VAR inspection_done = false
VAR inspect_phase = 1   // 1 = police report, 2 = bank statement


// ===============================================================
//  INSPECTION-01 · Missing Person Report
// ===============================================================

// Witness statement — "She left around nine."
=== inspect_01_A ===
{ seen_01_A: -> DONE }
~ seen_01_A = true
MC (inner) : Her friend's statement. She left around nine, heading straight home because of a morning lab.
-> check_timeline

// Family statement — "Sierra never came home that night."
=== inspect_01_B ===
{ seen_01_B: -> DONE }
~ seen_01_B = true
MC (inner) : Her mother's statement. Sierra never came home that night. Not even a text from the train.
-> check_timeline

=== check_timeline ===
{ seen_01_A && seen_01_B && not ev_timeline:
    ~ ev_timeline = true
    MC (inner) : Her friend says she left for home at nine, but her family says she never returned home.. how odd. #evidence:TIMELINE
    { ev_location && not said_two_hours:
        -> two_hours ->
    }
}
-> check_done

// Note 4.1 — last device activity 23:40  ->  LOCATION
// (INSPECTION-02 is skipped for now, so the phone-log lines play here)
=== inspect_01_C ===
{ seen_01_C: -> DONE }
~ seen_01_C = true
~ ev_location = true
MC (inner) : Huh..but her phone pinged at 11.40 at...here? That's nowhere near her home. Why did she go that far? #evidence:LOCATION
{ ev_timeline && not said_two_hours:
    -> two_hours ->
}
-> check_done

// Note 4.2 — the mirror purchase  -> opens the bank statement
=== inspect_01_D ===
{ seen_01_D: -> DONE }
~ seen_01_D = true
MC (inner) : A purchase from her bank records? I'll need to look at that statement.
-> check_done

// Note 4.3 — "cashier input error"
=== inspect_01_E ===
{ seen_01_E || seen_03_B: -> DONE }
~ seen_01_E = true
-> input_error_rant

// Priority: Standard  (optional)
=== inspect_01_F ===
{ seen_01_F: -> DONE }
~ seen_01_F = true
MC (inner) : "Priority: Standard." Three weeks gone, and she's still just... standard.
-> check_done

// Victim photo  (optional)
=== inspect_01_G ===
{ seen_01_G: -> DONE }
~ seen_01_G = true
MC (inner) : Twenty-one. A pharmacy student with a morning lab the next day.
MC (inner) : Girls like that don't just walk away from their lives.
-> check_done


// ===============================================================
//  INSPECTION-02 · Device Activity Log
// ===============================================================

// 21:06 Riverside Station  (optional)
=== inspect_02_A ===
{ seen_02_A: -> DONE }
~ seen_02_A = true
MC (inner) : 21:06, Riverside Station. So she really was on her way home.
-> check_done

// Gap 21:06 – 22:30  (optional)
=== inspect_02_B ===
{ seen_02_B: -> DONE }
~ seen_02_B = true
{ ev_location && ev_timeline && not said_two_hours:
    -> two_hours ->
- else:
    MC (inner) : Nothing at all for almost an hour and a half.
}
-> check_done

// 23:40 last registration  ->  LOCATION
=== inspect_02_C ===
{ seen_02_C: -> DONE }
~ seen_02_C = true
~ ev_location = true
MC (inner) : Huh.. but her phone pinged at 11:40 at... here?
MC (inner) : That's nowhere near her home. Why did she go that far? #evidence:LOCATION
{ ev_timeline && not said_two_hours:
    -> two_hours ->
}
-> check_done

// No registration after 23:41  (optional)
=== inspect_02_D ===
{ seen_02_D: -> DONE }
~ seen_02_D = true
MC (inner) : And after that... nothing. Not a single tower picked her up again.
-> check_done

// Map — Old Shopping District  (optional)
=== inspect_02_E ===
{ seen_02_E: -> DONE }
~ seen_02_E = true
MC (inner) : Riverside first, then she turned away from home. Toward the old shopping district.
-> check_done

// Plays once, as soon as both TIMELINE and LOCATION are known
=== two_hours ===
~ said_two_hours = true
MC (inner) : Two hours and forty minutes. Where could you have been.. Sierra..
->->


// ===============================================================
//  INSPECTION-03 · Bank Statement
// ===============================================================

// 08 SEP — ANTIQUE HAND MIRROR FOR SALE
=== inspect_03_A ===
{ seen_03_A: -> DONE }
~ seen_03_A = true
MC (inner) : This was extracted from her bank account.
MC (inner) : "Antique hand mirror." Written right there in the description, a week before she disappeared.
-> check_mirror

// Police sticky note
=== inspect_03_B ===
{ seen_03_B || seen_01_E: -> DONE }
~ seen_03_B = true
-> input_error_rant

// 15 SEP — last ordinary purchase  (optional)
=== inspect_03_C ===
{ seen_03_C: -> DONE }
~ seen_03_C = true
MC (inner) : Lunch at the canteen. Just another ordinary day.
-> check_done

// Shared by 01_E and 03_B (whichever the player finds first)
=== input_error_rant ===
MC (inner) : The police noted that the shop owner has never stocked mirrors. So it was logged as a cashier input error.
MC (inner) : Error my ass.. there's no way they could've let it slide that easily.
MC (inner) : How could anyone "mistype" something that wasn't even there to begin with? This doesn't make any sense at all..
-> check_mirror

=== check_mirror ===
{ seen_03_A && (seen_01_E || seen_03_B) && not ev_mirror:
    ~ ev_mirror = true
    MC (inner) : A mirror nobody ever sold. Bought one week before she vanished. #evidence:MIRROR
}
-> check_done


// ===============================================================
//  COMPLETION
// ===============================================================
=== check_done ===
{
    - inspect_phase == 1 && ev_timeline && ev_location && seen_01_D:
        -> report_complete
    - inspect_phase == 2 && ev_mirror:
        -> inspection_complete
}
-> DONE

// Police report finished: it closes, then prologue.ink plays
// "That should be all.. let's check her account statement too"
=== report_complete ===
~ inspect_phase = 2
~ inspection_done = true
-> DONE

// Bank statement finished
=== inspection_complete ===
~ inspect_phase = 3
~ inspection_done = true
MC (inner) : That's it. Two things that don't add up.
-> DONE
