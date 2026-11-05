// Test de calib_view.h (page Calibration d'ar6dof_app) : lecture des fichiers d'etat ecrits par la sonde, projection de la mire, geometrie de l'apercu.
// Les JSON viennent du VRAI moteur (calib_session.h), pas d'un exemple ecrit a la main.
#include <cstdio>
#include <fstream>
#include <sstream>

#include "calib_session.h"
#include "app/calib_view.h"

static int g_fail = 0;
#define CHECK(c, ...) do { if (!(c)) { g_fail++; std::printf("ECHEC : "); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)

static std::string ReadAll(const char* p) {
  std::ifstream f(p);
  std::stringstream ss;
  ss << f.rdbuf();
  return ss.str();
}

int main(int argc, char** argv) {
  using namespace calview;

  // 1) etat « capture » produit par le moteur
  ar6cal::Session s;
  ar6cal::Board b; b.cols = 9; b.rows = 6; b.squareMm = 27.5;
  s.Start(b, 120, 18, 60.0);
  Status st;
  CHECK(ParseStatus(s.StatusJson(), st), "lecture de l'etat capture");
  CHECK(st.state == "capture", "etat %s", st.state.c_str());
  CHECK(st.cols == 9 && st.rows == 6 && std::fabs(st.squareMm - 27.5) < 1e-6, "grille %dx%d carreau %.2f", st.cols, st.rows, st.squareMm);
  CHECK(st.goalCount == 16 && st.target == 18 && st.goal == 0, "objectifs %d cible %d goal %d", st.goalCount, st.target, st.goal);
  int ng = 0;
  Goals(&ng);
  CHECK(ng == st.goalCount, "la table des poses (%d) doit avoir autant d'entrees que le moteur (%d)", ng, st.goalCount);
  CHECK(DecideStep(true, st, false) == kCapture && DecideStep(false, st, false) == kPrep, "etape capture / sonde absente");
  s.Cancel();
  Status idle;
  CHECK(ParseStatus(s.StatusJson(), idle) && idle.state == "idle" && DecideStep(true, idle, false) == kPrep, "etat repos");

  // 2) etat « termine » (deverse par calib_session_test avec AR6_DUMP_STATUS)
  const char* path = argc > 1 ? argv[1] : nullptr;
  if (path) {
    std::string js = ReadAll(path);
    Status d;
    CHECK(ParseStatus(js, d), "lecture de l'etat termine");
    CHECK(d.state == "done", "etat %s", d.state.c_str());
    CHECK(d.result.ok && d.result.score > 0 && d.result.total >= 10 && d.result.used >= 10, "resultat ok=%d score=%d vues %d/%d", d.result.ok, d.result.score, d.result.used, d.result.total);
    CHECK(std::fabs(d.result.baselineMm - 60.0) < 1.5, "base %.2f", d.result.baselineMm);
    CHECK(!d.result.model.empty(), "modele de distorsion");
    CHECK(d.result.rmsL > 0 && d.result.rmsL < 2 && d.result.epi >= 0 && d.result.hfov > 20 && d.result.hfov < 200, "rms %.3f epi %.3f hfov %.1f", d.result.rmsL, d.result.epi, d.result.hfov);
    CHECK(DecideStep(true, d, false) == kResult && DecideStep(true, d, true) == kVerify, "etapes resultat / verification");
    std::printf("[done] score %d, %d/%d vues, base %.2f mm, modele %s, %zu avertissement(s), verif seq %d ratio %.3f\n", d.result.score, d.result.used, d.result.total,
                d.result.baselineMm, d.result.model.c_str(), d.result.warnings.size(), d.verifySeq, d.verifyRatio);
    for (const auto& w : d.result.warnings) CHECK(WarningText(w).fr[0] != 0, "avertissement inconnu : %s", w.c_str());
  } else {
    std::printf("(pas de fichier d'etat termine donne : cette partie est sautee)\n");
  }

  // 3) coins normalises, chaines, tableaux
  Status c;
  CHECK(ParseStatus("{\"state\":\"capture\",\"msg\":\"still\",\"goal\":3,\"goalsDone\":5,\"progress\":0.25,\"foundL\":true,\"foundR\":false,\"corners\":[0.1,0.2,0.3,0.4],"
                    "\"error\":\"a\\\"b\\\\c\",\"verify\":{\"seq\":2,\"estMm\":812.5,\"ratio\":1.0156},\"result\":{\"ok\":false,\"score\":0,\"warnings\":[\"x\",\"y_z\"]}}", c), "json a la main");
  CHECK(c.corners.size() == 4 && std::fabs(c.corners[3] - 0.4f) < 1e-6 && c.foundL && !c.foundR && c.goal == 3 && c.msg == "still", "champs simples");
  CHECK(c.error == "a\"b\\c", "echappements : %s", c.error.c_str());
  CHECK(c.verifySeq == 2 && std::fabs(c.verifyEstMm - 812.5) < 1e-9 && std::fabs(c.verifyRatio - 1.0156) < 1e-9, "verification");
  CHECK(c.result.warnings.size() == 2 && c.result.warnings[1] == "y_z", "avertissements");
  CHECK(PopCount(c.goalsDone) == 2, "popcount %d", PopCount(c.goalsDone));
  CHECK(!ParseStatus("n'importe quoi", c) && !ParseStatus("", c), "texte invalide refuse");

  // 4) calib_info.json
  Info in;
  CHECK(ParseInfo("{\"source\":\"ar6dof\",\"model\":\"rational\",\"calibW\":800,\"calibH\":600,\"runW\":400,\"runH\":300,\"scale\":0.5000,\"mismatch\":true,\"baselineMm\":60.2000,"
                  "\"rms\":0.2,\"epi\":0.1,\"views\":17,\"fps\":60,\"runFps\":120,\"squareMm\":27.5,\"scaleCorr\":1.01,\"hfov\":100.0,\"created\":\"2026-11-05\",\"error\":\"\","
                  "\"zoom\":1.9800,\"zoomSource\":\"auto\",\"zoomAuto\":false,\"zoomConfirmed\":true,\"zoomN\":55,\"zoomMotion\":1.2,\"zoomSpread\":0.05,\"zoomResidual\":1.01}", in), "info");
  CHECK(in.calibW == 800 && in.runW == 400 && in.fps == 60 && in.runFps == 120 && in.mismatch && !in.zoomAuto && in.zoomConfirmed && std::fabs(in.zoom - 1.98) < 1e-9 && in.zoomSource == "auto",
        "champs de l'info");

  // 5) mire en perspective : de face, centree et symetrique ; lacet positif = bord droit plus proche = plus grand
  Pose p;
  p.size = 0.5;
  double ax, ay, bx, by;
  ProjectBoard(p, 10, 7, -1, 0, 0, 0, 200, 150, ax, ay);
  ProjectBoard(p, 10, 7, 1, 0, 0, 0, 200, 150, bx, by);
  CHECK(std::fabs((ax + bx) / 2 - 100) < 1e-9 && std::fabs(ay - 75) < 1e-9 && std::fabs(by - 75) < 1e-9 && std::fabs((bx - ax) - 100) < 1e-9, "mire de face : %.2f..%.2f", ax, bx);
  ProjectBoard(p, 10, 7, 0, 1, 0, 0, 200, 150, ax, ay);
  CHECK(ay < 75, "le haut de la mire est vers le haut de l'ecran (y %.2f)", ay);
  p.yaw = 42;
  double rx0, ry0, rx1, ry1, lx0, ly0, lx1, ly1;
  ProjectBoard(p, 10, 7, 1, -1, 0, 0, 200, 150, rx0, ry0);
  ProjectBoard(p, 10, 7, 1, 1, 0, 0, 200, 150, rx1, ry1);
  ProjectBoard(p, 10, 7, -1, -1, 0, 0, 200, 150, lx0, ly0);
  ProjectBoard(p, 10, 7, -1, 1, 0, 0, 200, 150, lx1, ly1);
  CHECK(std::fabs(ry1 - ry0) > std::fabs(ly1 - ly0), "lacet positif : bord droit plus grand (%.1f contre %.1f)", std::fabs(ry1 - ry0), std::fabs(ly1 - ly0));
  Pose q;
  q.size = 0.5;
  q.pitch = 38;   // tangage positif : bord du haut plus loin = plus petit
  double tx0, ty0, tx1, ty1, ux0, uy0, ux1, uy1;
  ProjectBoard(q, 10, 7, -1, 1, 0, 0, 200, 150, tx0, ty0);
  ProjectBoard(q, 10, 7, 1, 1, 0, 0, 200, 150, tx1, ty1);
  ProjectBoard(q, 10, 7, -1, -1, 0, 0, 200, 150, ux0, uy0);
  ProjectBoard(q, 10, 7, 1, -1, 0, 0, 200, 150, ux1, uy1);
  CHECK(std::fabs(tx1 - tx0) < std::fabs(ux1 - ux0), "tangage positif : le haut est plus petit (%.1f contre %.1f)", std::fabs(tx1 - tx0), std::fabs(ux1 - ux0));
  // toutes les poses restent dans le rectangle
  const GoalInfo* gi = Goals(&ng);
  for (int i = 0; i < ng; i++) {
    Pose pg;
    pg.px = gi[i].px; pg.py = gi[i].py; pg.size = gi[i].size; pg.yaw = gi[i].yaw; pg.pitch = gi[i].pitch; pg.roll = gi[i].roll;
    for (double sx : {-1.0, 1.0})
      for (double sy : {-1.0, 1.0}) {
        double ox, oy;
        ProjectBoard(pg, 10, 7, sx, sy, 0, 0, 400, 300, ox, oy);
        CHECK(ox > -2 && ox < 402 && oy > -2 && oy < 302, "pose %d : coin (%.0f, %.0f) hors de l'image", i, ox, oy);
      }
    CHECK(gi[i].name.fr[0] && gi[i].name.en[0] && gi[i].title.fr[0] && gi[i].title.en[0] && gi[i].tip.fr[0] && gi[i].tip.en[0], "textes de la pose %d", i);
  }

  // 6) apercu 800x300 : image stereo 2 x (800x600) -> 800x300 sans bandes... (1600x600 : 2.67 = 800/300) ; 2 x (640x480) -> bandes laterales
  EyeRect e = PreviewEye(800, 600, false);
  CHECK(std::fabs(e.x) < 1e-9 && std::fabs(e.w - 400) < 1e-9 && std::fabs(e.h - 300) < 1e-9, "apercu 1600x600 : %.1f %.1f %.1f %.1f", e.x, e.y, e.w, e.h);
  e = PreviewEye(800, 600, true);
  CHECK(std::fabs(e.x - 400) < 1e-9, "yeux inverses : l'oeil gauche de la sonde est la moitie droite (%.1f)", e.x);
  e = PreviewEye(640, 480, false);
  CHECK(std::fabs(e.h - 300) < 1e-9 && std::fabs(e.w - 400) < 1e-9 && e.x >= 0, "apercu 1280x480 : %.1f %.1f %.1f %.1f", e.x, e.y, e.w, e.h);
  e = PreviewEye(1600, 600, false);
  CHECK(e.w > 0 && e.w <= 400 && e.h > 0 && e.h <= 300 && e.y >= 0 && std::fabs(e.w / e.h - 1600.0 / 600.0) < 0.05, "apercu 3200x600 : %.1f %.1f %.1f %.1f", e.x, e.y, e.w, e.h);

  // 7) textes
  for (const char* m : {"show", "showR", "still", "move", "ok", "edge", "small", "swapped", "order", "parity", "idle"})
    CHECK(MessageText(m).fr[0] && MessageText(m).en[0], "message %s", m);
  CHECK(Verdict(90).fr[0] && Verdict(10).en[0] && StageText("termine").fr[0] && Num(1.236, 2) == L"1.24", "divers");

  std::printf(g_fail ? "\n%d ECHEC(S)\n" : "\nTOUS LES TESTS DE LA VUE DE CALIBRATION OK\n", g_fail);
  return g_fail ? 1 : 0;
}
