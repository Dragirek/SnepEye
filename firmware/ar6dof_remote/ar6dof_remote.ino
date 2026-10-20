// ar6dof_remote.ino : télécommande pointeur gyroscopique SANS FIL (ESP32-C3 Supermini + BNO085 + 2 boutons).
//
// Envoie en WiFi (UDP) l'orientation du BNO085 (vecteur de rotation "jeu" : référence la gravité, le cap est relatif)
// et l'état des 2 boutons, ~100 fois par seconde. Aucune adresse IP à configurer :
//   - tant que le PC n'a pas répondu, les paquets partent en diffusion (broadcast) sur le réseau local ;
//   - Unity répond "A6ACK" : la télécommande bascule alors sur une liaison directe vers ce PC.
// Il faut seulement que le PC et la télécommande soient sur le MÊME réseau WiFi 2,4 GHz (et que le pare-feu Windows
// autorise Unity en UDP, une fenêtre le propose au premier lancement).
//
// Câblage (à adapter ci-dessous) : BNO085 SDA -> GPIO5, SCL -> GPIO6, 3V3, GND.
//   Bouton GAUCHE -> GPIO3 et GND ; bouton DROIT -> GPIO4 et GND (résistances internes activées).
// Arduino IDE : bibliothèque "Adafruit BNO08x". Carte : ESP32C3 Dev Module.
//
// PAQUET (28 octets, little-endian) :
//   0 uint32 magic 0x52365241 ('A','R','6','R')   4 uint8 version = 1   5 uint8 boutons (bit0 gauche, bit1 droit)
//   6 uint16 numéro de séquence   8 float qw, qx, qy, qz (16 octets)   24 uint32 micros()

#include <WiFi.h>
#include <WiFiUdp.h>
#include <Wire.h>
#include <Adafruit_BNO08x.h>

// ---- À MODIFIER ----
const char* WIFI_SSID = "NOM_DE_TON_WIFI";
const char* WIFI_PASS = "MOT_DE_PASSE_WIFI";
// --------------------
#define PC_PORT 9878
#define LOCAL_PORT 9879
#define PIN_SDA 5
#define PIN_SCL 6
#define PIN_BTN_LEFT 3
#define PIN_BTN_RIGHT 4
#define BNO_RESET_PIN -1                 // GPIO relié à la broche RST du BNO085, ou -1
#define REPORT_INTERVAL_US 8000          // 125 Hz

Adafruit_BNO08x bno08x(BNO_RESET_PIN);
sh2_SensorValue_t sv;
WiFiUDP udp;
IPAddress pcIp;
bool unicast = false;
uint32_t lastAckMs = 0;
uint16_t seq = 0;
float q[4] = {1, 0, 0, 0};
bool haveQuat = false;
uint8_t lastButtons = 0;

void enableReports() { bno08x.enableReport(SH2_GAME_ROTATION_VECTOR, REPORT_INTERVAL_US); }

void connectWifi() {
  WiFi.mode(WIFI_STA);
  WiFi.setSleep(false);                  // indispensable : l'économie d'énergie du WiFi ajoute de 10 à 100 ms de latence
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  uint32_t t0 = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - t0 < 15000) delay(100);
}

void sendPacket(uint8_t buttons) {
  uint8_t b[28];
  uint32_t magic = 0x52365241u, us = micros();
  memcpy(b, &magic, 4);
  b[4] = 1;
  b[5] = buttons;
  memcpy(b + 6, &seq, 2);
  memcpy(b + 8, q, 16);
  memcpy(b + 24, &us, 4);
  seq++;
  IPAddress target = unicast ? pcIp : IPAddress(255, 255, 255, 255);
  udp.beginPacket(target, PC_PORT);
  udp.write(b, sizeof(b));
  udp.endPacket();
}

void setup() {
  pinMode(PIN_BTN_LEFT, INPUT_PULLUP);
  pinMode(PIN_BTN_RIGHT, INPUT_PULLUP);
  Wire.begin(PIN_SDA, PIN_SCL);
  delay(300);                            // laisse le BNO085 démarrer
  while (!(bno08x.begin_I2C(0x4A, &Wire) || bno08x.begin_I2C(0x4B, &Wire))) delay(500);
  Wire.setClock(400000);
  enableReports();
  connectWifi();
  udp.begin(LOCAL_PORT);
}

void loop() {
  if (WiFi.status() != WL_CONNECTED) { unicast = false; connectWifi(); return; }
  if (bno08x.wasReset()) enableReports();

  // Réponse du PC : on passe de la diffusion à l'envoi direct
  int n = udp.parsePacket();
  if (n > 0) {
    char buf[8] = {0};
    udp.read(buf, min(n, 7));
    if (memcmp(buf, "A6ACK", 5) == 0) { pcIp = udp.remoteIP(); unicast = true; lastAckMs = millis(); }
  }
  if (unicast && millis() - lastAckMs > 3000) unicast = false;   // PC disparu : on rediffuse

  uint8_t buttons = (digitalRead(PIN_BTN_LEFT) == LOW ? 1 : 0) | (digitalRead(PIN_BTN_RIGHT) == LOW ? 2 : 0);
  bool send = (buttons != lastButtons);   // un changement de bouton part tout de suite
  lastButtons = buttons;

  while (bno08x.getSensorEvent(&sv)) {
    if (sv.sensorId == SH2_GAME_ROTATION_VECTOR) {
      q[0] = sv.un.gameRotationVector.real;
      q[1] = sv.un.gameRotationVector.i;
      q[2] = sv.un.gameRotationVector.j;
      q[3] = sv.un.gameRotationVector.k;
      haveQuat = true;
      send = true;
    }
  }
  if (send && haveQuat) sendPacket(buttons);
}
