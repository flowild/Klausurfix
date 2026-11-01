namespace Klausurfix.Core.Models;

public enum StudentExamStatus
{
    Graded = 1,          // Bewertet / Regulär teilgenommen
    Missing = 2,         // Krank / Entschuldigt gefehlt
    NotParticipated = 3, // Nicht mitgeschrieben / Unentschuldigt
    Incomplete = 4       // Unvollständig / Noch in Bearbeitung
}

