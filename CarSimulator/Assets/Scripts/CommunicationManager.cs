﻿
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using System.Net.Sockets;

public class CommunicationManager : MonoBehaviour {

	const int PORT = 38698;
	const int BUFFER_SIZE = 1 << 18;
	const byte SIMULATOR_RECORD = 30;
	const byte SIMULATOR_DRIVE = 31;

	public CarSteering car;
	public GameObject reconnectButton;
	public Toggle fastForwardButton;
	public float connectionTimeout = 20;
	public RenderTexture cameraView;
	public TrackManager track;
	public Speedometer speedometer;
	[Range(0f,1f)]
	public float sendInterval = 0.1f;
	[Range(1f, 20f)]
	public float fastForwardSpeed = 5f;

	Thread thread;
	byte[] buffer;
	Texture2D texture;
	bool requireTexture;
	float lastSend;
	int imageSize;
	bool requireScore;
	bool setupFastForward;

	void OnEnable () {
		reconnectButton.SetActive(false);
		fastForwardButton.isOn = false;
		fastForwardButton.gameObject.SetActive(false);
		if (buffer == null) buffer = new byte[BUFFER_SIZE];
		if (thread != null && thread.IsAlive) thread.Abort();
		if (texture == null) texture = new Texture2D(cameraView.width, cameraView.height);
		imageSize = texture.width * texture.height;
		requireTexture = false;
		requireScore = false;
		setupFastForward = false;
		DisableFastForward();
		thread = new Thread(Thread);
		thread.Start();
	}

	private void OnDestroy()
	{
		if (thread != null && thread.IsAlive)
			thread.Abort();
	}

	private void Update()
	{
		if(requireTexture && Time.timeScale > 0 && lastSend < Time.time)
		{
			lastSend = Time.time + sendInterval;
			RenderTexture.active = cameraView;
			texture = new Texture2D(cameraView.width, cameraView.height);
			texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
			texture.Apply();
			for (int i = 0; i < texture.width; i++)
			{
				for (int j = 0; j < texture.height; j++)
				{
					Color32 c = texture.GetPixel(i, j);
					int index = 4 * (i + texture.width * j);
					buffer[index] = c.r;
					buffer[index+1] = c.g;
					buffer[index+2] = c.b;
					buffer[index+3] = c.a;
				}
			}
			requireTexture = false;
		}
		else if(requireScore) 
		{
			if (Time.timeScale == 0)
			{
				Time.timeScale = fastForwardButton.isOn? fastForwardSpeed : 1;
				requireScore = false;
			}
			else
			{
				int score = (int)(track.CompleteBatch()*100f);
				buffer[3] = (byte)(score >> 24);
				buffer[2] = (byte)(score >> 16);
				buffer[1] = (byte)(score >> 8);
				buffer[0] = (byte)(score >> 0);
				requireScore = false;
				Time.timeScale = 0;
			}
		}
		else if (setupFastForward)
		{
			fastForwardButton.gameObject.SetActive(true);
			fastForwardButton.isOn = false;
			fastForwardButton.onValueChanged.RemoveAllListeners();
			fastForwardButton.onValueChanged.AddListener((v) =>
			{
				if (v)
					EnableFastForward();
				else
					DisableFastForward();
			});
			setupFastForward = false;
		}
		if (thread == null || !thread.IsAlive)
		{
			if (!car.userInput) car.userInput = true;
			reconnectButton.SetActive(true);
			enabled = false;
			thread = null;
			fastForwardButton.gameObject.SetActive(false);
			DisableFastForward();
		}
	}

	void Thread()
	{
		Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		try
		{
			socket.Connect("localhost", PORT);
			socket.Receive(buffer);
			track.ResetScore();
			switch (buffer[0])
			{
				case SIMULATOR_RECORD:
					car.userInput = true;
					while (car.verticalSteering == 0f) ;
					while (true)
					{
						FillStatusBuffer();
						if (socket.Send(buffer, imageSize * 4 + 5, SocketFlags.None) == 0)
							break;
						if (socket.Receive(buffer) == 0)
							break;
					}
					break;
				case SIMULATOR_DRIVE:
					setupFastForward = true;
					car.userInput = false;
					while (true)
					{
						FillStatusBuffer();
						if (socket.Send(buffer, imageSize * 4 + 5, SocketFlags.None) == 0)
							break;
						int size = socket.Receive(buffer);
						if (size == 2)
						{
							car.horizontalSteering = ((float)buffer[0]) / 127.5f - 1f;
							car.verticalSteering = ((float)buffer[1]) / 127.5f - 1f;
						}
						else if (size == 1)
						{
							requireScore = true;
							while (requireScore) System.Threading.Thread.Yield();
							if (socket.Send(buffer, 4, SocketFlags.None) == 0)
								break;
							if (socket.Receive(buffer) == 0)
								break;
							requireScore = true;
						}
						else
							break;
					}
					break;
			}
		}
		catch (ThreadAbortException)
		{
			thread = null;
		}
		catch (SocketException)
		{
			thread = null;
		}
		catch (System.Exception e)
		{
			thread = null;
			Debug.LogException(e);
		}
		finally
		{
			socket.Shutdown(SocketShutdown.Both);
			socket.Close();
			thread = null;
		}
	}

	void FillStatusBuffer()
	{
		requireTexture = true;
		while (requireTexture) System.Threading.Thread.Yield();
		buffer[imageSize * 4 + 0] = (byte)((track.directionVector.x + 1) * 127.5f);
		buffer[imageSize * 4 + 1] = (byte)((track.directionVector.y + 1) * 127.5f);
		buffer[imageSize*4 + 2] = (byte)(speedometer.speed*3+100);
		buffer[imageSize*4 + 3] = (byte)(car.horizontalSteering * 127.5f + 127.5f);
		buffer[imageSize*4 + 4] = (byte)(car.verticalSteering * 127.5f + 127.5f);
	}


	void EnableFastForward()
	{
		fastForwardButton.isOn = true;
		if (Time.timeScale > 0)
			Time.timeScale = fastForwardSpeed;
		QualitySettings.vSyncCount = 0;
		Time.fixedDeltaTime = 0.02f / fastForwardSpeed;
		Camera.main.rect = new Rect(0.4f, 0.4f, 0.2f, 0.2f);
		AudioListener.pause = true;
	}

	void DisableFastForward()
	{
		fastForwardButton.isOn = false;
		if (Time.timeScale > 0)
			Time.timeScale = 1;
		Time.fixedDeltaTime = 0.01f;
		Camera.main.rect = new Rect(0, 0, 1, 1);
		AudioListener.pause = false;
	}
}