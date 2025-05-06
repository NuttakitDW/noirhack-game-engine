using System;
using System.Collections.Generic;

[Serializable]
public class StartShufflePayload
{
    public string agg_pk;
    public List<string[]> deck;
}

[Serializable]
public class IncomingFrame<T>
{
    public int type;
    public string target;
    public T[] arguments;
}

[Serializable]
public class ShuffleDonePayload
{
    public List<string[]> encrypted_deck;
    public List<string> public_inputs;
    public string proof;
}
