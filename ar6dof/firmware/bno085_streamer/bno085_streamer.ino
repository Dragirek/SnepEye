// bno085_streamer.ino  (v2)
//
// Envoie accéléromètre + gyroscope CALIBRÉS du BNO085 en USB série,
// avec un timestamp (micros() de l'ESP32) sur chaque échantillon.
//
// Changements par rapport à la v1 :
//  - timestamp uint32 en microsecondes dans chaque paquet (indispensable au VIO)
//  - un paquet par échantillon réel (plus de valeur "périmée" répétée
//    quand l'autre capteur n'a pas encore répondu)
//  - en-tête 2 octets (0xAA 0x55) + type, resynchronisation fiable côté PC
//  - plus de "hello world" ni de while(!Serial) bloquant
//  - I2C à 400 kHz, 200 Hz par défaut (voir REPORT_INTERVAL_US)
//
// Arduino IDE (ESP32-C3 Supermini) :
//   Outils > USB CDC On Boot > Enabled   (obligatoire pour Serial via l'USB-C)
//   Bibliothèque : "Adafruit BNO08x"
//
// FORMAT DU PAQUET (20 octets, little-endian) :
//   [0]      0xAA
//   [1]      0x55
//   [2]      type : 1 = accel (m/s^2), 2 = gyro (rad/s)
//   [3..6]   uint32 timestamp en µs (micros(), boucle toutes les ~71 min)
//   [7..18]  float32 x3  : x, y, z
//   [19]     uint8 : XOR des octets [2..18]

#include <Adafruit_BNO08x.h>

#define I2C_SDA_PIN 8
#define I2C_SCL_PIN 9
// GPIO8/9 sont des pins de strapping sur l'ESP32-C3. Si la carte ne démarre
// plus une fois le BNO085 câblé, déplace SDA/SCL sur deux autres GPIO libres.

// 5000 µs = 200 Hz (la cadence standard en VIO). Monte à 2500 (400 Hz)
// seulement si le compteur de Hz côté PC montre que tout passe sans perte.
#define REPORT_INTERVAL_US 5000

Adafruit_BNO08x bno08x;
sh2_SensorValue_t sv;

enum : uint8_t { PKT_ACCEL = 1, PKT_GYRO = 2 };

void setReports() {
  bno08x.enableReport(SH2_ACCELEROMETER, REPORT_INTERVAL_US);
  bno08x.enableReport(SH2_GYROSCOPE_CALIBRATED, REPORT_INTERVAL_US);
}

void sendPacket(uint8_t type, uint32_t t_us, float x, float y, float z) {
  uint8_t buf[20];
  float v[3] = {x, y, z};
  buf[0] = 0xAA;
  buf[1] = 0x55;
  buf[2] = type;
  memcpy(buf + 3, &t_us, 4);
  memcpy(buf + 7, v, 12);
  uint8_t c = 0;
  for (int i = 2; i < 19; i++) c ^= buf[i];
  buf[19] = c;
  Serial.write(buf, sizeof(buf));
}

void setup() {
  Serial.begin(2000000);  // ignoré en USB CDC natif, sans effet néfaste
  Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN);

  // Si le BNO085 n'est pas trouvé : message texte toutes les secondes
  // (le PC affichera "octets reçus mais 0 paquet valide").
  while (!bno08x.begin_I2C()) {
    Serial.println("BNO085 NOT FOUND");
    delay(1000);
  }
  Wire.setClock(400000);
  setReports();
}

void loop() {
  if (bno08x.wasReset()) setReports();

  while (bno08x.getSensorEvent(&sv)) {
    uint32_t t = micros();
    if (sv.sensorId == SH2_ACCELEROMETER) {
      sendPacket(PKT_ACCEL, t, sv.un.accelerometer.x, sv.un.accelerometer.y,
                 sv.un.accelerometer.z);
    } else if (sv.sensorId == SH2_GYROSCOPE_CALIBRATED) {
      sendPacket(PKT_GYRO, t, sv.un.gyroscope.x, sv.un.gyroscope.y,
                 sv.un.gyroscope.z);
    }
  }
}
