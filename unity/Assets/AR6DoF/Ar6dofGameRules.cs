using System.Collections.Generic;

namespace AR6DoF
{
    /// <summary>Un schema des regles : un petit plateau 2D. Chaque case est un mot separe par des espaces : un caractere de piece (ou « . » pour vide) suivi de suffixes
    /// « o » point vert (coup possible), « r » fond rouge, « g » fond vert, « y » fond jaune.
    /// Pieces selon 'kind' : 0 echecs (PNBRQK blancs, pnbrqk noirs), 1 dames (w / W blanc pion / dame, b / B noir), 2 morpion (X, O), 3 puissance 4 (R, Y),
    /// 4 demineur (# cachee, 1 a 8, F drapeau, M mine, . vide ouverte), 5 machine a sous (0 a 5 = symbole), 6 de (o = point), 7 bataille navale (# navire, ~ eau, m rate, H touche, S coule).</summary>
    public sealed class Ar6dofRuleDiagram
    {
        public int kind;
        public string caption;
        public string[] rows;
    }

    /// <summary>Une page de regles (un onglet) : titre, texte et schemas.</summary>
    public sealed class Ar6dofRulePage
    {
        public string tab, title, text;
        public Ar6dofRuleDiagram[] diagrams;
    }

    /// <summary>Regles de chaque jeu, avec schemas. Les textes sont des cles de traduction comme le reste de l'interface.</summary>
    public static class Ar6dofRules
    {
        static Ar6dofRuleDiagram D(int kind, string caption, params string[] rows) { return new Ar6dofRuleDiagram { kind = kind, caption = caption, rows = rows }; }

        static Ar6dofRulePage P(string tab, string title, string text, params Ar6dofRuleDiagram[] d)
        {
            return new Ar6dofRulePage { tab = tab, title = title, text = text, diagrams = d };
        }

        public static Ar6dofRulePage[] For(string id)
        {
            switch (id)
            {
                case "tictactoe":
                    return new[]
                    {
                        P("But", "Aligner trois symboles",
                          "Chacun son tour, pose ton symbole dans une case de la grille 3 x 3. Le premier qui aligne trois symboles, en ligne, en colonne ou en diagonale, gagne. Si la grille est pleine sans alignement, c'est nul. Niveaux : Facile joue au hasard, Moyen se defend, Imbattable ne perd jamais.",
                          D(2, "Une diagonale", "Xg O .", "O Xg .", ". . Xg"),
                          D(2, "Une ligne", "Xg Xg Xg", "O O .", ". . ."),
                          D(2, "Une colonne", "O Xg .", "O Xg .", ". Xg O"))
                    };

                case "dice":
                    return new[]
                    {
                        P("Utilisation", "Lancer les des",
                          "Choisis le type de de (piece, d4, d6, d8, d10, d12, d20 ou d100) et le nombre de des (de 1 a 6), puis lance. Un dN donne un nombre de 1 a N au hasard, et le total s'affiche. La piece donne pile ou face.",
                          D(6, "d6 : de 1 a 6", "o . o", ". o .", "o . o"),
                          D(6, "Un six", "o . o", "o . o", "o . o"),
                          D(6, "Un trois", "o . .", ". o .", ". . o"))
                    };

                case "slots":
                    return new[]
                    {
                        P("Gains", "Comment gagner",
                          "Choisis ta mise puis lance. Si les trois symboles du milieu sont identiques, tu gagnes ta mise multipliee par le gain du symbole (la table est sous les boutons). Deux cerises donnent x2. Sinon, la mise est perdue. Les jetons sont virtuels et ne valent rien. Quand il t'en manque, Recharger en offre 100.",
                          D(5, "Trois identiques : gagne", "0 0 0"),
                          D(5, "Le gros lot", "5 5 5"),
                          D(5, "Rien d'aligne : perdu", "0 3 1"))
                    };

                case "battleship":
                    return new[]
                    {
                        P("But", "Couler la flotte ennemie",
                          "Chaque camp cache cinq navires sur une grille de 10 x 10 : porte-avions (5 cases), croiseur (4), destroyer (3), sous-marin (3) et patrouilleur (2). La partie commence des l'ouverture : tant que tu n'as pas tire, tu peux melanger ta flotte jusqu'a ce qu'elle te plaise. A tour de role, tu tires sur une case de la flotte ennemie. Le premier qui coule tous les navires adverses gagne.",
                          D(7, "Les 5 navires", "# # # # #", "# # # # ~", "# # # ~ ~", "# # # ~ ~", "# # ~ ~ ~")),
                        P("Tirs", "Rate, touche, coule",
                          "Un rond blanc veut dire a l'eau. Un navire rouge avec du feu est touche : tente les cases voisines. Quand toutes les cases d'un navire sont touchees, il est coule : il explose puis ne fume plus. Tu ne vois que ce que tu as touche de la flotte ennemie ; la tienne est toujours visible.",
                          D(7, "A l'eau, touche, coule", "m ~ S S", "~ H ~ ~", "~ ~ ~ ~")),
                        P("Animations", "Les tirs",
                          "Chaque tir part en animation : missile, avion ou canon, toujours le meme vol que le tir touche ou non, pour ne rien reveler. Le resultat n'apparait qu'a l'impact, et rien ne bloque la partie : tu peux continuer a jouer pendant que les obus volent.")
                    };

                case "chess":
                    return new[]
                    {
                        P("Pieces", "Les deplacements",
                          "Chaque camp commence avec 16 pieces. La tour va tout droit, le fou en diagonale, le cavalier saute en L par-dessus les autres. La dame cumule la tour et le fou, le roi avance d'une case. On prend une piece adverse en arrivant sur sa case.",
                          D(0, "La tour", ". . o . .", ". . o . .", "o o R o o", ". . o . .", ". . o . ."),
                          D(0, "Le fou", "o . . . o", ". o . o .", ". . B . .", ". o . o .", "o . . . o"),
                          D(0, "Le cavalier", ". o . o .", "o . . . o", ". . N . .", "o . . . o", ". o . o .")),
                        P("Pion", "Le pion et les coups speciaux",
                          "Le pion avance d'une case (de deux depuis sa case de depart) mais prend en diagonale. Arrive au bout du plateau, il est promu en dame. Prise en passant : un pion qui vient d'avancer de deux cases peut etre pris comme s'il n'en avait avance qu'une. Roque : le roi se deplace de deux cases vers une tour, qui passe de l'autre cote, si ni l'un ni l'autre n'a bouge et si le roi ne passe pas par une case attaquee.",
                          D(0, "Le pion", ". . . . .", ". . o . .", ". pr o . .", ". . P . .", ". . . . ."),
                          D(0, "Petit roque", "R . . . K . . R", "R . . . . Rg Kg .")),
                        P("Fin", "Echec, mat et nulle",
                          "Echec : ton roi est attaque, tu dois le sauver (deplacer le roi, prendre l'attaquant ou t'interposer). Echec et mat : impossible de le sauver, la partie est perdue. Pat : tu n'as aucun coup possible sans etre en echec, c'est nul. Nulle aussi apres 50 coups sans prise ni pion, ou s'il ne reste pas assez de pieces pour mater.",
                          D(0, "Echec et mat", "kr . . R", "p p . ."))
                    };

                case "checkers":
                    return new[]
                    {
                        P("Francaises", "Dames francaises",
                          "On joue sur les cases sombres. Le pion avance d'une case en diagonale vers l'avant, mais il prend aussi en arriere en sautant par-dessus un pion adverse. La prise est obligatoire, et il faut prendre le maximum de pieces possible. La dame va aussi loin qu'elle veut en diagonale et prend a distance : elle saute une piece adverse et se pose sur n'importe quelle case libre derriere. Un pion devient dame s'il finit son coup sur la derniere rangee.",
                          D(1, "Prise en arriere", ". . . . . .", ". . . . . .", ". . . . . .", ". . w . . .", ". . . br . .", ". . . . .o ."),
                          D(1, "Dame : prise a distance", ". . . . . .o", ". . . . .o .", ". . . .o . .", ". . br . . .", ". . . . . .", "W . . . . .")),
                        P("Americaines", "Dames americaines",
                          "Le pion avance et prend uniquement vers l'avant, d'une case. La dame se deplace et prend d'une seule case a la fois, en avant comme en arriere. La prise est obligatoire, et on doit enchainer les sauts tant que c'est possible, mais on n'est pas oblige de choisir la plus grosse prise. Un pion qui atteint la derniere rangee devient dame.",
                          D(1, "Pion : prise vers l'avant", ". . . o .", ". . br . .", ". w . . .", ". . b . .", ". . . . ."),
                          D(1, "Dame : une case", ". . . . .", "o . o . .", ". W . . .", "o . o . .", ". . . . .")),
                        P("Fin", "Fin de partie",
                          "On gagne quand l'adversaire n'a plus de piece, ou plus aucun coup possible. La partie est nulle apres 40 coups sans prise ni mouvement de pion. Le bouton Regles de la fenetre de jeu choisit les regles francaises ou americaines (une nouvelle partie commence).")
                    };

                case "connect4":
                    return new[]
                    {
                        P("But", "Aligner quatre jetons",
                          "Chacun son tour, lache un jeton dans une colonne : il tombe tout en bas, sur le dernier jeton pose. Le premier qui aligne quatre jetons, en ligne, en colonne ou en diagonale, gagne. Si la grille est pleine sans alignement, c'est nul. Niveaux : Facile, Moyen, Fort.",
                          D(3, "Une ligne", ". . . . .", ". . . . .", "Y Y . . .", "Rg Rg Rg Rg Y"),
                          D(3, "Une colonne", "Rg . .", "Rg Y .", "Rg Y .", "Rg Y Y"),
                          D(3, "Une diagonale", ". . . Rg", ". . Rg Y", ". Rg Y Y", "Rg Y R Y"))
                    };

                case "mines":
                    return new[]
                    {
                        P("But", "Eviter les mines",
                          "Des mines sont cachees sous les cases. Ouvre toutes les cases qui n'en ont pas pour gagner ; en ouvrir une qui en a une fait perdre. La premiere case que tu ouvres n'est jamais une mine. Une case vide s'ouvre en cascade avec ses voisines.",
                          D(4, "Cases cachees et ouvertes", "# # # # #", "# 1 1 1 #", "# 1 . 1 #", "# 1 1 1 #")),
                        P("Chiffres", "Lire les chiffres",
                          "Un chiffre dit combien de mines touchent la case, parmi ses 8 voisines. Si le chiffre est egal au nombre de cases cachees autour, toutes sont des mines : pose-y des drapeaux (outil Drapeau, ou bouton droit du laser). Une fois tous les drapeaux d'un chiffre poses, touche ce chiffre pour ouvrir d'un coup ses autres voisines.",
                          D(4, "Un 1 : la mine est la", "1 F", "1 1"),
                          D(4, "Un 2 : deux mines autour", "# # #", "# 2 #", "# # #"))
                    };
            }
            return null;
        }

        /// <summary>Toutes les cles de traduction utilisees (pour verification).</summary>
        public static IEnumerable<string> AllKeys()
        {
            foreach (var id in new[] { "tictactoe", "dice", "slots", "battleship", "chess", "checkers", "connect4", "mines" })
            {
                var pages = For(id);
                if (pages == null) continue;
                foreach (var p in pages)
                {
                    yield return p.tab; yield return p.title; yield return p.text;
                    if (p.diagrams != null) foreach (var d in p.diagrams) yield return d.caption;
                }
            }
        }
    }
}
