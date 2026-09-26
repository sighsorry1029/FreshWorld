// Native persistent-event data only; no Unity lifecycle or game spawning is simulated here.
public class PersistentEventSystem
{
    public static PersistentEventSystem instance = new();
    public List<PersistentEvent> m_possibleEvents = new() { new() { internalName = "jotun_invasion" } };
    public ActiveEventsList m_activePersistentEvents = new();
    public class PersistentEvent { public string internalName = ""; }
    public class ActiveEventsList { public List<ActivePersistentEvent> list = new(); }
    public class ActivePersistentEvent
    {
        public int sourceEventId;
        public int eventId;
        public UnityEngine.Vector3 position;
        public float radius;
    }
}
