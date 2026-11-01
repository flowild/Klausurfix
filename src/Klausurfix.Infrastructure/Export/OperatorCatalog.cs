namespace Klausurfix.Infrastructure.Export;

using Klausurfix.Core.Models;

/// <summary>Transcribed from the supplied PDF, not from the older sample DOCX.</summary>
public static class OperatorCatalog
{
    public const string Source = "Hessisches Ministerium für Kultus, Bildung und Chancen · Landesabitur 2027 · Deutsch, Musik, Sport und Fachbereich II · Stand: 1. August 2025";
    public static IReadOnlyList<ExamOperator> All { get; } = Array.AsReadOnly(new ExamOperator[]
    {
        new("berechnen", "berechnen", "anhand vorgegebener Daten durch Rechenoperationen zu einem Ergebnis gelangen und die Rechenschritte dokumentieren", "I–II"),
        new("beschreiben", "beschreiben", "Aussagen, Sachverhalte, Situationen, Strukturen o. Ä. in eigenen Worten strukturiert und fachsprachlich darlegen", "I–II"),
        new("nennen", "nennen", "zielgerichtet Informationen zusammentragen, ohne diese zu kommentieren", "I"),
        new("notieren", "notieren", "Noten und musikalische Zeichen traditionell oder graphisch aufschreiben", "I–II"),
        new("skizzieren", "skizzieren", "einen Sachverhalt oder Gedankengang in seinen Grundzügen angeben", "I–II"),
        new("wiedergeben", "wiedergeben", "ausgehend von einem Einleitungssatz Informationen aus dem vorliegenden Material unter Verwendung der Fachsprache in eigenen Worten ausdrücken", "I"),
        new("zusammenfassen", "zusammenfassen", "ausgehend von einem Einleitungssatz die wesentlichen Inhalte und Aussagen eines Textes in strukturierter und komprimierter Form unter Verwendung der Fachsprache in eigenen Worten herausstellen", "I–II"),
        new("analysieren", "analysieren", "Materialien, Texte, Sachverhalte, Zusammenhänge o. Ä. als Ganzes oder aspektgeleitet bzw. kriterienorientiert erschließen und das Ergebnis der Erschließung darlegen (in Deutsch: unter Wahrung des funktionalen Zusammenhangs von Inhalt, Form und Sprache)", "I–III"),
        new("anwenden", "anwenden", "einen bekannten Sachverhalt oder eine bekannte Methode auf eine neue Problemstellung beziehen", "II"),
        new("auswerten", "auswerten", "Daten, Einzelergebnisse oder Sachverhalte zu einer abschließenden Gesamtaussage zusammenführen", "II"),
        new("charakterisieren", "charakterisieren", "die jeweilige Eigenart von Figuren, Sachverhalten o. Ä. verdeutlichen", "II–III"),
        new("darstellen", "darstellen", "Sachverhalte o. Ä. und deren Bezüge sowie Zusammenhänge aufzeigen", "I–II"),
        new("einordnen", "einordnen / zuordnen", "Texte oder Sachverhalte unter Verwendung von Vorwissen begründet in einen genannten Zusammenhang stellen", "I–II"),
        new("erklaeren", "erklären", "Materialien, Sachverhalte o. Ä. in einen Begründungszusammenhang stellen, z. B. durch Rückführung auf fachliche Grundprinzipien, Gesetzmäßigkeiten, Funktionszusammenhänge, Modelle oder Regeln", "II"),
        new("erlaeutern", "erläutern", "Materialien, Sachverhalte, Zusammenhänge, Thesen o. Ä. mit zusätzlichen Informationen und Beispielen veranschaulichen", "II–III"),
        new("gliedern", "gliedern", "ein Musikstück begründet in Abschnitte einteilen und diese sprachlich bezeichnen", "I–II"),
        new("herausarbeiten", "herausarbeiten", "aus Materialien nicht explizit genannte Sachverhalte erschließen", "II"),
        new("in-beziehung-setzen", "in Beziehung setzen", "Zusammenhänge unter vorgegebenen oder selbst gewählten Gesichtspunkten begründet herstellen", "II–III"),
        new("untersuchen", "untersuchen", "Sachverhalte unter bestimmten Aspekten betrachten und belegen", "II"),
        new("vergleichen", "vergleichen / gegenüberstellen", "nach vorgegebenen oder selbst gewählten Gesichtspunkten Gemeinsamkeiten, Ähnlichkeiten und Unterschiede begründet darlegen", "II–III"),
        new("begruenden", "begründen", "einen Sachverhalt bzw. eine Aussage durch Argumente stützen", "II–III"),
        new("beurteilen", "beurteilen", "zu einem Sachverhalt oder einer Aussage auf Basis von Kriterien und unter Verwendung von Fachwissen und Fachmethoden eine begründete Einschätzung geben", "II–III"),
        new("bewerten", "bewerten / Stellung nehmen", "wie Operator ‚beurteilen‘, aber zusätzlich die eigenen Maßstäbe begründet darlegen", "II–III"),
        new("diskutieren", "diskutieren / sich auseinandersetzen mit", "zu einer Aussage, Problemstellung oder These eine Argumentation entwickeln, die zu einer begründeten Bewertung führt", "II–III"),
        new("entwickeln", "entwickeln", "einen eigenen Gedankengang bzw. ein Konzept zu einem Thema entfalten und Schlussfolgerungen ziehen", "II–III"),
        new("eroertern", "erörtern", "ggf. auf Grundlage einer Materialanalyse oder -auswertung eine These oder Problemstellung unter Abwägen von Pro- und Kontraargumenten hinterfragen und zu einem eigenen Urteil gelangen", "I–III"),
        new("gestalten", "gestalten / entwerfen", "Aufgabenstellungen kreativ und produktorientiert bearbeiten, z. B. auf der Grundlage eines Materials und seiner inhaltlichen oder stilistischen Gegebenheiten eine kreative Idee in ein selbstständiges Produkt umsetzen", "III"),
        new("interpretieren", "interpretieren", "auf der Grundlage einer Analyse Sinnzusammenhänge aus Materialien methodisch reflektiert erschließen, um zu einer schlüssigen Gesamtauslegung zu gelangen", "I–III"),
        new("komponieren", "komponieren", "ein Musikstück verfassen, ggf. unter Einbeziehung vorgegebener Merkmale", "III"),
        new("ueberpruefen", "überprüfen", "Aussagen/Behauptungen auf der Grundlage von Fachkenntnissen kritisch hinterfragen und auf ihre Angemessenheit hin begründet einschätzen", "II–III"),
        new("verfassen", "verfassen", "auf der Grundlage einer Auswertung von Materialien wesentliche Aspekte eines Sachverhaltes in informierender oder argumentierender Form darlegen", "I–III")
    });
}
