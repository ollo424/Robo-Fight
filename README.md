# Robo-Fight

2D izometrik arena üzerinde farklı sınıflardaki robotların birbirleriyle savaştığı, Unity tabanlı bir proje.

Bu proje temel olarak şu başlıkları içerir:
- Prosedürel arena üretimi
- Grid tabanlı pathfinding + steering hareket
- Sınıf bazlı combat sistemi (Sword, ShieldKnife, Gunner, Bomber)
- Sprite listesiyle yönlü karakter ve efekt animasyonları
- Round yönetimi, kazanan takibi ve otomatik restart

## Kullanılan Teknolojiler

- **Unity**: `6000.0.39f1`
- **Dil**: C#
- **Render Pipeline**: URP (`com.unity.render-pipelines.universal`)
- **2D Paketleri**: `com.unity.feature.2d`, `physics2d`, `tilemap`, `ugui`

## Oynanış Özeti

Arena oluşturulduktan sonra robotlar spawn olur ve otomatik olarak birbirleriyle savaşır.

Robot sınıfları:
- **Sword**: Yakın dövüş saldırısı
- **ShieldKnife**: Yakın dövüş + önden gelen hasarı azaltma
- **Gunner**: Mermi atışı
- **Bomber**: Bomba atışı ve alan etkisi

## Proje Yapısı

Ana scriptler `My project/Assets/Scripts/` altında:

- `Core/`
  - `GameManager.cs`: Oyun akışı, spawn, round sonu, skor/win list
  - `IsometricArenaController.cs`: Arena kurulum ve spawn noktaları
  - `RoadGenerator.cs`: Road/inner area/building üretimi, grid pathfinding yardımcıları
  - `YSortSpriteOrder.cs`: Y konumuna göre sorting order
  - `AgentDebugLogger.cs`: NDJSON debug log yazımı

- `Robots/`
  - `MechaUnitController.cs`: AI kararları (hedef seçimi, melee/ranged davranışları)
  - `SteeringMovementController.cs`: Rigidbody2D tabanlı hareket ve obstacle avoidance
  - `MechaCombatController.cs`: Saldırı, hasar, sınıf bazlı combat mekanikleri
  - `MechaDirectionalSpriteAnimator.cs`: Yönlü sprite-list animasyonu

- `Combat/`
  - `MechaProjectile.cs`: Gunner mermisi davranışı
  - `MechaBombProjectile.cs`: Bomber bombası, fuse ve patlama
  - `MechaAreaExplosion.cs`: Alan hasarı (yardımcı patlama davranışı)
  - `DirectionalOneShotEffect.cs`: Tek seferlik yönlü efekt animasyonları

## Kurulum ve Çalıştırma

1. Bu repoyu klonlayın:
   - `git clone <repo-url>`
2. Unity Hub üzerinden `My project` klasörünü açın.
3. Unity sürümünü `6000.0.39f1` ile eşleştirmeniz önerilir.
4. Sahneyi (`SampleScene`) açın.
5. Play ile simülasyonu başlatın.

## Konfigürasyon Notları

- **Spawn / Arena**
  - `RoadGenerator` içindeki `innerTileWidth`, `innerTileHeight`, `spawnInsetFromInnerEdge`, `spawnClearanceCells` ayarları spawn alanı ve koruma tamponunu etkiler.

- **Hareket**
  - Robot hızları `SteeringMovementController.moveSpeed` üzerinden ayarlanır.
  - Combat scriptinde saldırı anı hız düşümü için `enableAttackMoveSlow` aç/kapa yapılabilir.

- **Ranged davranışı**
  - `MechaUnitController` üzerindeki ranged random/patrol parametreleri ile edge/building/enemy uzaklığı ayarlanabilir.

- **Combat**
  - Her sınıf için saldırı cooldown alanları `MechaCombatController` içindedir.
  - Gunner projectile yönü sprite önü sağ olacak şekilde ayarlanmıştır.

## Debug / Log

Projede `AgentDebugLogger` ile dosyaya yazılan debug kayıtları vardır:
- Varsayılan log yolu: repo kökünde `debug-6c05a1.log`
- Format: NDJSON (satır satır JSON)

Bu log sistemi geliştirme/debug sürecinde kullanılmıştır.

## Bilinen Geliştirme Alanları

- Pathfinding + local avoidance etkileşimini daha da stabil hale getirme
- Robot AI davranışlarının daha stratejik hale getirilmesi
- UI/UX iyileştirmeleri (durum göstergeleri, class bazlı info paneli vb.)

## Lisans

Bu repo ödev/prototip amaçlı hazırlanmıştır. Lisans gereksiniminiz varsa ek lisans dosyası (`LICENSE`) ekleyebilirsiniz.

<img width="1552" height="867" alt="Ekran görüntüsü 2026-05-27 205006" src="https://github.com/user-attachments/assets/0dd6c293-bcda-4498-a2e5-30cf50603bc1" />

<img width="1550" height="869" alt="Ekran görüntüsü 2026-05-27 205015" src="https://github.com/user-attachments/assets/04747edd-4429-42b4-a4f7-a6e9c4d88209" />
